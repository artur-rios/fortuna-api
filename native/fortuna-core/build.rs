use std::env;
use std::path::PathBuf;

fn main() {
    let crate_dir = PathBuf::from(env::var("CARGO_MANIFEST_DIR").expect("manifest directory"));
    let config = cbindgen::Config::from_file(crate_dir.join("cbindgen.toml"))
        .expect("valid cbindgen configuration");
    let header = crate_dir.join("include/fortuna_core.h");

    cbindgen::Builder::new()
        .with_crate(&crate_dir)
        .with_config(config)
        .generate()
        .expect("generate Fortuna C header")
        .write_to_file(header);

    println!("cargo:rerun-if-changed=src/lib.rs");
    println!("cargo:rerun-if-changed=cbindgen.toml");
}
