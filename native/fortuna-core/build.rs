use std::env;
use std::fs;
use std::path::PathBuf;

fn main() {
    let crate_dir = PathBuf::from(env::var("CARGO_MANIFEST_DIR").expect("manifest directory"));
    let contract = crate_dir.join("../../docs/openapi/fortuna.json");
    let operations = read_operations(&contract);
    let generated = generate_rust(&operations);
    fs::write(
        PathBuf::from(env::var("OUT_DIR").expect("output directory"))
            .join("generated_operations.rs"),
        generated,
    )
    .expect("write generated native operations");

    let config = cbindgen::Config::from_file(crate_dir.join("cbindgen.toml"))
        .expect("valid cbindgen configuration");
    let header = crate_dir.join("include/fortuna_core.h");

    let bindings = cbindgen::Builder::new()
        .with_crate(&crate_dir)
        .with_config(config)
        .generate()
        .expect("generate Fortuna C header");
    bindings.write_to_file(&header);
    append_operation_declarations(&header, &operations);

    println!("cargo:rerun-if-changed=src/lib.rs");
    println!("cargo:rerun-if-changed=src/operation.rs");
    println!("cargo:rerun-if-changed=cbindgen.toml");
    println!("cargo:rerun-if-changed={}", contract.display());
}

#[derive(Debug)]
struct Operation {
    symbol: String,
    method: String,
    path: String,
    area: String,
    long_running: bool,
    response_schema: String,
}

fn read_operations(contract: &PathBuf) -> Vec<Operation> {
    let document: serde_json::Value = serde_json::from_str(
        &fs::read_to_string(contract).expect("read checked-in OpenAPI contract"),
    )
    .expect("parse checked-in OpenAPI contract");
    let mut operations = Vec::new();
    let paths = document["paths"].as_object().expect("OpenAPI paths object");
    for (path, path_item) in paths {
        let methods = path_item.as_object().expect("OpenAPI path item");
        for method in ["get", "post", "put", "patch", "delete"] {
            let Some(operation) = methods.get(method) else {
                continue;
            };
            if exclusion_reason(method, path).is_some() {
                continue;
            }
            let area = operation["tags"]
                .as_array()
                .and_then(|tags| tags.first())
                .and_then(serde_json::Value::as_str)
                .unwrap_or("Other")
                .to_owned();
            let response_schema = operation["responses"]
                .as_object()
                .and_then(|responses| {
                    responses
                        .iter()
                        .filter(|(status, _)| status.starts_with('2'))
                        .find_map(|(_, response)| {
                            response["content"]
                                .as_object()
                                .and_then(|content| content.values().next())
                                .and_then(|media| media["schema"]["$ref"].as_str())
                        })
                })
                .and_then(|reference| reference.rsplit('/').next())
                .unwrap_or("")
                .to_owned();
            operations.push(Operation {
                symbol: symbol(method, path),
                method: method.to_ascii_uppercase(),
                path: path.to_owned(),
                area,
                long_running: matches!(
                    path.as_str(),
                    "/api/imports/excel" | "/api/imports/pdf" | "/api/exports"
                ),
                response_schema,
            });
        }
    }
    operations.sort_by(|left, right| {
        left.path
            .cmp(&right.path)
            .then(left.method.cmp(&right.method))
    });
    operations
}

fn exclusion_reason(_method: &str, path: &str) -> Option<&'static str> {
    if path.starts_with("/api/auth") {
        return Some("connected identity requires Heimdall");
    }
    if path.starts_with("/api/connections") || path == "/api/data-sources" {
        return Some("Pluggy operations require a remote service");
    }
    if path == "/api/me/consents" || path.starts_with("/api/me/consents/") {
        return Some("external-processing consent applies only to hosted integrations");
    }
    if path.starts_with("/api/users") {
        return Some("offline installations have no instance administrator");
    }
    match path {
        "/api/exchange-rates/sync" => Some("rate synchronization requires a remote source"),
        "/api/local-accounts/password-reset" => Some("local recovery uses recovery codes"),
        "/healthcheck" | "/healthcheck/detailed" => {
            Some("host health is represented by fortuna_health")
        }
        _ => None,
    }
}

fn symbol(method: &str, path: &str) -> String {
    let mut parts = vec!["fortuna".to_owned()];
    for part in path.trim_matches('/').split('/') {
        if let Some(parameter) = part
            .strip_prefix('{')
            .and_then(|value| value.strip_suffix('}'))
        {
            parts.push("by".to_owned());
            parts.push(to_snake_case(parameter));
        } else {
            parts.push(to_snake_case(part));
        }
    }
    parts.push(method.to_owned());
    parts.join("_")
}

fn to_snake_case(value: &str) -> String {
    let mut result = String::new();
    for (index, character) in value.chars().enumerate() {
        if character == '-' {
            result.push('_');
        } else if character.is_ascii_uppercase() {
            if index > 0 {
                result.push('_');
            }
            result.push(character.to_ascii_lowercase());
        } else {
            result.push(character);
        }
    }
    result
}

fn generate_rust(operations: &[Operation]) -> String {
    let mut output = String::from("// Generated from docs/openapi/fortuna.json. Do not edit.\n\n");
    output.push_str("pub(crate) static NATIVE_OPERATIONS: &[OperationSpec] = &[\n");
    for operation in operations {
        output.push_str(&format!(
            "    OperationSpec {{ symbol: {:?}, method: {:?}, path: {:?}, area: {:?}, long_running: {}, response_schema: {:?} }},\n",
            operation.symbol, operation.method, operation.path, operation.area, operation.long_running, operation.response_schema
        ));
    }
    output.push_str("];\n\n");
    for operation in operations {
        output.push_str(&format!(
            "#[doc = \"Mirror `{} {}` over the native C ABI.\"]\n#[unsafe(no_mangle)]\npub extern \"C\" fn {}(request_json: *const c_char, response_json: *mut *mut c_char) -> c_int {{\n    operation_call(OperationSpec {{ symbol: {:?}, method: {:?}, path: {:?}, area: {:?}, long_running: {}, response_schema: {:?} }}, request_json, response_json)\n}}\n\n",
            operation.method,
            operation.path,
            operation.symbol,
            operation.symbol,
            operation.method,
            operation.path,
            operation.area,
            operation.long_running,
            operation.response_schema
        ));
    }
    output.push_str(
        "#[cfg(test)]\npub(crate) static NATIVE_OPERATION_FUNCTIONS: &[NativeOperationFunction] = &[\n",
    );
    for operation in operations {
        output.push_str(&format!("    {},\n", operation.symbol));
    }
    output.push_str("];\n");
    output
}

fn append_operation_declarations(header: &PathBuf, operations: &[Operation]) {
    let mut contents = fs::read_to_string(header).expect("read generated header");
    let marker = "#endif  /* FORTUNA_CORE_H */";
    let mut declarations = String::from(
        "\n#ifdef __cplusplus\nextern \"C\" {\n#endif  // __cplusplus\n\n/**\n * Offline route exports generated from docs/openapi/fortuna.json.\n * Request metadata uses {token, route, query, body}; body is the unchanged HTTP JSON body.\n * Deliberately unavailable: /api/auth/** (Heimdall), /api/connections/** and\n * GET /api/data-sources (Pluggy), POST /api/exchange-rates/sync (remote rate source),\n * DELETE /api/users/{id} (no offline administrator), /api/me/consents/** (hosted\n * external processing only), POST /api/local-accounts/password-reset (use recovery\n * codes), and the HTTP-host health routes.\n * Call fortuna_capabilities to discover the machine-readable availability contract.\n */\n",
    );
    for operation in operations {
        declarations.push_str(&format!(
            "int {}(const char *request_json, char **response_json); /* {} {} */\n",
            operation.symbol, operation.method, operation.path
        ));
    }
    declarations.push_str("\n#ifdef __cplusplus\n}  // extern \"C\"\n#endif  // __cplusplus\n\n");
    contents = contents.replacen(marker, &(declarations + marker), 1);
    fs::write(header, contents).expect("append native operation declarations");
}
