# Privacy

## Data Processing

SolidworksArucoGenerator operates locally inside SOLIDWORKS. It does not:

- connect to external servers;
- collect telemetry or analytics;
- require an account or login;
- upload generated models, images, parameters, or file paths.

Generated SLDPRT, PNG, and STEP files are written only to the output directory
selected by the user.

## Local System Changes

The installer:

- copies the add-in to `%ProgramData%\Codex\ArucoSolidWorksAddin`;
- registers the managed COM server;
- creates the SOLIDWORKS add-in and startup registry entries;
- creates a Windows uninstall entry.

The uninstaller removes these files and registry entries.

## Publication Review

The published source and installer were checked for credentials, access
tokens, private keys, email addresses, local Windows usernames, and
machine-specific absolute paths. Build directories, PDB files, local logs,
validation outputs, generated CAD files, and registry exports are excluded.

The public installer is not Authenticode-signed.
