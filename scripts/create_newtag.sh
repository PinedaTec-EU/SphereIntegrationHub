#!/bin/bash

VERSION="$1"

# Directorio del script
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION_FILE="${SCRIPT_DIR}/version.nfo"

# Si no se pasa la versión, leerla del fichero version.nfo
if [ -z "$VERSION" ]; then
	if [ ! -f "$VERSION_FILE" ]; then
		echo "Error: No existe el fichero version.nfo"
		exit 1
	fi
	VERSION=$(cat "$VERSION_FILE" | tr -d '[:space:]')
	if [ -z "$VERSION" ]; then
		echo "Error: version.nfo está vacío"
		exit 1
	fi
fi

TAG="deploy/v${VERSION}"

echo "Creando tag: $TAG"

# Verifica si el tag ya existe
if git rev-parse "$TAG" >/dev/null 2>&1; then
	echo "Error: el tag $TAG ya existe. Actualiza version.nfo antes de volver a ejecutar."
	exit 1
fi

# Actualiza version.nfo con la nueva versión
echo "$VERSION" > "$VERSION_FILE"

git add "$VERSION_FILE"
git commit -m "chore: bump version to $VERSION"
git push

git tag "$TAG"
git push --tags

echo "✓ Tag creado y pusheado: $TAG"