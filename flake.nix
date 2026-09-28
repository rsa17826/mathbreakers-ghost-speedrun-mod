{
  inputs = {
    nixpkgs = {
      url = "github:NixOS/nixpkgs/nixos-unstable";
    };
    flake-utils = {
      url = "github:numtide/flake-utils";
    };
  };

  outputs =
    {
      nixpkgs,
      flake-utils,
      ...
    }:
    flake-utils.lib.eachDefaultSystem (
      system:
      let
        pkgs = import nixpkgs { inherit system; };

      in
      {
        devShells = {
          default = pkgs.mkShell {
            packages = with pkgs; [
              (python313.withPackages (ps: [
                (pkgs.python313Packages.buildPythonPackage rec {
                  pname = "UnityPy";
                  version = "1.25.3";

                  pyproject = true;

                  src = pkgs.fetchPypi {
                    pname = "unitypy";
                    inherit version;
                    hash = "sha256-QbPoMKBEad+r6twJf1m6cAjiiYZGBHKOGtYjwi18MNw=";
                  };

                  build-system = with pkgs.python313Packages; [
                    setuptools
                  ];

                  doCheck = false;

                  # Skip strict check for optional runtime packages missing in nixpkgs
                  dontCheckRuntimeDeps = true;

                  propagatedBuildInputs = with pkgs.python313Packages; [
                    attrs
                    brotli
                    fsspec
                    lz4
                    pillow
                    pycryptodome
                  ];
                })
                (pkgs.python313Packages.buildPythonPackage rec {
                  pname = "tpk-ar";
                  version = "0.2.4"; # Adjust if a specific version is required

                  pyproject = true;

                  src = pkgs.fetchPypi {
                    pname = "tpk_ar"; # PyPI distribution name uses underscore
                    inherit version;
                    hash = "sha256-1kiX5aC83dWOin1N2SLzctqYSg+YvDtGDshpqrkG7ng="; # Replace with real hash
                  };

                  build-system = with pkgs.python313Packages; [
                    setuptools
                  ];

                  doCheck = false;
                })
              ]))
              dotnet-sdk_8
              ilspycmd
              avalonia-ilspy
              mono
              csharpier
            ];
          };
        };
      }
    );
}
