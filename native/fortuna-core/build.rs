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
    let mut generated_header = Vec::new();
    bindings.write(&mut generated_header);
    let contents = append_operation_declarations(
        String::from_utf8(generated_header).expect("UTF-8 generated header"),
        &operations,
    );
    // Only touch the header when its content changes. The header is also watched below, so an
    // unconditional write would leave it newer than this run and rerun the script on every build.
    if fs::read_to_string(&header).ok().as_deref() != Some(contents.as_str()) {
        fs::write(&header, contents).expect("write generated Fortuna C header");
    }

    // cbindgen parses the whole crate, so any source file can change the header.
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-changed=src");
    println!("cargo:rerun-if-changed=cbindgen.toml");
    println!("cargo:rerun-if-changed={}", contract.display());
    // Regenerate when the checked-in header is edited by hand or deleted.
    println!("cargo:rerun-if-changed={}", header.display());
}

#[derive(Debug)]
struct Operation {
    symbol: String,
    method: String,
    path: String,
    area: String,
    long_running: bool,
    response_schema: String,
    /// Why the native core exports this route but does not implement it. Such an export keeps
    /// the C ABI stable, answers `FORTUNA_STATUS_NOT_IMPLEMENTED` and is not reported as an
    /// available capability.
    not_implemented: Option<&'static str>,
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
                not_implemented: not_implemented_reason(method, path),
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

/// Operations the native core exports for ABI stability but does not implement. Each answers
/// `FORTUNA_STATUS_NOT_IMPLEMENTED` (501) instead of reporting success for work it did not do,
/// and `fortuna_capabilities` lists it under `notImplemented` rather than `operations`.
fn not_implemented_reason(method: &str, path: &str) -> Option<&'static str> {
    const IMPORTS: &str = "File ingestion is not implemented by the native core: it has no workbook or PDF statement parser, so an offline import would not be processed.";
    const EXPORTS: &str = "Data-set exports are not implemented by the native core; the personal data archive (POST /api/me/data-export) is available offline.";
    const REPORTS: &str = "Reports and projections are not computed by the native core.";
    const TRANSFERS: &str = "Transfers are not implemented by the native core: it does not create the paired transaction legs, currency conversion and lifecycle cascade of a transfer.";
    const INSTALLMENTS: &str = "Installment plans are not implemented by the native core: it does not split a purchase into installments assigned to billing cycles.";
    const STATEMENTS: &str = "Credit-card statements are not implemented by the native core: it does not assign charges to billing cycles, close or settle them.";
    const MATERIALIZATION: &str = "Recurring occurrences are not materialized by the native core.";
    const PLANNING: &str =
        "Budget consumption and goal progress are not computed by the native core.";
    const RECONCILIATION: &str =
        "Reconciliation needs imported records, which the native core does not create.";
    match (method, path) {
        ("post", "/api/imports/excel" | "/api/imports/pdf") => Some(IMPORTS),
        ("post", "/api/import-jobs/{id}/retry") => Some(IMPORTS),
        ("post", "/api/exports") => Some(EXPORTS),
        (_, path) if path.starts_with("/api/reports/") || path.starts_with("/api/projections/") => {
            Some(REPORTS)
        }
        (_, path) if path.starts_with("/api/transfers") => Some(TRANSFERS),
        (_, path) if path.starts_with("/api/installment-plans") => Some(INSTALLMENTS),
        (_, path) if path.starts_with("/api/statements/") => Some(STATEMENTS),
        ("get", "/api/credit-cards/{id}/statements") => Some(STATEMENTS),
        ("post", "/api/recurring-transactions/materialize") => Some(MATERIALIZATION),
        ("get", "/api/budgets/{id}/consumption" | "/api/goals/{id}/progress") => Some(PLANNING),
        ("post", "/api/transactions/{id}/reconcile") => Some(RECONCILIATION),
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
    let spec = |operation: &Operation| {
        format!(
            "OperationSpec {{ symbol: {:?}, method: {:?}, path: {:?}, area: {:?}, long_running: {}, response_schema: {:?} }}",
            operation.symbol,
            operation.method,
            operation.path,
            operation.area,
            operation.long_running,
            operation.response_schema
        )
    };
    let mut output = String::from("// Generated from docs/openapi/fortuna.json. Do not edit.\n\n");
    output.push_str("pub(crate) static NATIVE_OPERATIONS: &[OperationSpec] = &[\n");
    for operation in operations
        .iter()
        .filter(|operation| operation.not_implemented.is_none())
    {
        output.push_str(&format!("    {},\n", spec(operation)));
    }
    output.push_str("];\n\n");
    output.push_str(
        "pub(crate) static NOT_IMPLEMENTED_OPERATIONS: &[NotImplementedOperation] = &[\n",
    );
    for operation in operations {
        if let Some(reason) = operation.not_implemented {
            output.push_str(&format!(
                "    NotImplementedOperation {{ operation: {}, reason: {:?} }},\n",
                spec(operation),
                reason
            ));
        }
    }
    output.push_str("];\n\n");
    for operation in operations {
        let body = match operation.not_implemented {
            None => format!(
                "operation_call({}, request_json, response_json)",
                spec(operation)
            ),
            Some(reason) => format!(
                "not_implemented_call(NotImplementedOperation {{ operation: {}, reason: {:?} }}, response_json)",
                spec(operation),
                reason
            ),
        };
        // A route that is not implemented never reads its request.
        let (note, request) = if operation.not_implemented.is_some() {
            (
                " Not implemented offline: always answers `FORTUNA_STATUS_NOT_IMPLEMENTED`.",
                "_request_json",
            )
        } else {
            ("", "request_json")
        };
        output.push_str(&format!(
            "#[doc = \"Mirror `{} {}` over the native C ABI.{}\"]\n#[unsafe(no_mangle)]\npub extern \"C\" fn {}({}: *const c_char, response_json: *mut *mut c_char) -> c_int {{\n    {}\n}}\n\n",
            operation.method, operation.path, note, operation.symbol, request, body
        ));
    }
    for (name, implemented) in [
        ("NATIVE_OPERATION_FUNCTIONS", true),
        ("NOT_IMPLEMENTED_OPERATION_FUNCTIONS", false),
    ] {
        output.push_str(&format!(
            "#[cfg(test)]\npub(crate) static {name}: &[NativeOperationFunction] = &[\n"
        ));
        for operation in operations
            .iter()
            .filter(|operation| operation.not_implemented.is_none() == implemented)
        {
            output.push_str(&format!("    {},\n", operation.symbol));
        }
        output.push_str("];\n");
    }
    output
}

fn append_operation_declarations(contents: String, operations: &[Operation]) -> String {
    let marker = "#endif  /* FORTUNA_CORE_H */";
    // Glob patterns are spelled `/api/auth/...` rather than with `**`: a slash followed by an
    // asterisk inside a C block comment opens a nested comment, which compilers warn about.
    let mut declarations = String::from(
        "\n#ifdef __cplusplus\nextern \"C\" {\n#endif  // __cplusplus\n\n/**\n * Offline route exports generated from docs/openapi/fortuna.json.\n * Request metadata uses {token, route, query, body}; body is the unchanged HTTP JSON body.\n * Deliberately unavailable: /api/auth/... (Heimdall), /api/connections/... and\n * GET /api/data-sources (Pluggy), POST /api/exchange-rates/sync (remote rate source),\n * DELETE /api/users/{id} (no offline administrator), /api/me/consents/... (hosted\n * external processing only), POST /api/local-accounts/password-reset (use recovery\n * codes), and the HTTP-host health routes.\n *\n * Exported but not implemented offline: each of these always answers\n * FORTUNA_STATUS_NOT_IMPLEMENTED (501) with a failure envelope stating the reason, and\n * fortuna_capabilities lists it under notImplemented instead of operations:\n",
    );
    for operation in operations
        .iter()
        .filter(|operation| operation.not_implemented.is_some())
    {
        declarations.push_str(&format!(" *   {} {}\n", operation.method, operation.path));
    }
    declarations.push_str(
        " *\n * Call fortuna_capabilities to discover the machine-readable availability contract.\n */\n",
    );
    for operation in operations {
        declarations.push_str(&format!(
            "int {}(const char *request_json, char **response_json); /* {} {} */\n",
            operation.symbol, operation.method, operation.path
        ));
    }
    declarations.push_str("\n#ifdef __cplusplus\n}  // extern \"C\"\n#endif  // __cplusplus\n\n");
    contents.replacen(marker, &(declarations + marker), 1)
}
