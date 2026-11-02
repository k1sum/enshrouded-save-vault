#!/bin/sh
set -eu
cd "$(dirname "$0")/../server"
mkdir -p ../dist/linux-amd64 ../dist/linux-arm64
CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -trimpath -ldflags='-s -w' -o ../dist/linux-amd64/save-server ./cmd/save-server
CGO_ENABLED=0 GOOS=linux GOARCH=arm64 go build -trimpath -ldflags='-s -w' -o ../dist/linux-arm64/save-server ./cmd/save-server
