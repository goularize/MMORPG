#!/bin/bash
# Links the shared backend contracts into the Unity client (idempotent, safe to re-run).
# Shared/Models is intentionally NOT linked: it is server-side data code (C# records
# that Unity's compiler cannot build without extra polyfills). Add it here when needed.
set -euo pipefail

cd "$(dirname "$0")"

case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*)
        # Git Bash's `ln -s` silently copies files instead of linking, which would duplicate Shared/.
        echo "ERROR: On Windows run setup_client.bat (creates real directory junctions)." >&2
        exit 1
        ;;
esac

SHARED_FOLDERS=(Enums Math Network Constants)
TARGET_DIR="Client/Assets/Scripts/Shared"

echo "======================================="
echo "Setting up Unity Client Symlinks (Mac/Linux)"
echo "======================================="

mkdir -p "$TARGET_DIR"

for folder in "${SHARED_FOLDERS[@]}"; do
    if [ ! -d "Shared/$folder" ]; then
        echo "ERROR: Shared/$folder does not exist. Run this script from a full clone." >&2
        exit 1
    fi

    link="$TARGET_DIR/$folder"
    if [ -L "$link" ]; then
        echo "OK: $folder link already exists."
    elif [ -e "$link" ]; then
        echo "ERROR: $link exists but is not a link. Remove it and re-run." >&2
        exit 1
    else
        echo "Creating $folder link..."
        # Relative target keeps the link valid if the repository is moved.
        ln -s "../../../../Shared/$folder" "$link"
    fi
done

echo ""
echo "Setup Complete! You can now open the Client folder in Unity."
