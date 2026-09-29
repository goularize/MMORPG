#!/bin/bash
echo "======================================="
echo "Setting up Unity Client Symlinks (Mac/Linux)"
echo "======================================="

mkdir -p "Client/Assets/Scripts/Shared"

echo "Creating Network Symlink..."
ln -sf "../../../../Shared/Network" "Client/Assets/Scripts/Shared/Network"

echo "Creating Math Symlink..."
ln -sf "../../../../Shared/Math" "Client/Assets/Scripts/Shared/Math"

echo ""
echo "Setup Complete! You can now open the Client folder in Unity."
