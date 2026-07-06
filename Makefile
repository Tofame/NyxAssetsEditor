# Variables
APP_NAME=NyxAssetsEditor
CONFIGURATION=Release
DOTNET=dotnet
OUTPUT_DIR=publish

.DEFAULT_GOAL := help

.PHONY: help all build clean publish publish-win publish-linux publish-osx publish-osx-x64 publish-osx-arm64

help:
	@echo "Available commands:"
	@echo "  make build             - Build the project locally in Release mode"
	@echo "  make clean             - Clean build folders (bin, obj, publish)"
	@echo "  make publish           - Publish self-contained binaries for all platforms"
	@echo "  make publish-win       - Publish self-contained single-file binary for Windows x64"
	@echo "  make publish-linux     - Publish self-contained single-file binary for Linux x64"
	@echo "  make publish-osx       - Publish self-contained single-file binaries for macOS x64 and arm64"
	@echo "  make publish-osx-x64   - Publish self-contained single-file binary for macOS x64"
	@echo "  make publish-osx-arm64 - Publish self-contained single-file binary for macOS arm64"

all: publish

build:
	$(DOTNET) build NyxAssetsEditor.csproj -c $(CONFIGURATION)

clean:
	$(DOTNET) clean NyxAssetsEditor.csproj
	rm -rf $(OUTPUT_DIR)
	rm -rf bin obj

publish: publish-win publish-linux publish-osx

publish-win:
	$(DOTNET) publish NyxAssetsEditor.csproj -c $(CONFIGURATION) -r win-x64 --self-contained true -p:PublishSingleFile=true -o $(OUTPUT_DIR)/win-x64

publish-linux:
	$(DOTNET) publish NyxAssetsEditor.csproj -c $(CONFIGURATION) -r linux-x64 --self-contained true -p:PublishSingleFile=true -o $(OUTPUT_DIR)/linux-x64

publish-osx: publish-osx-x64 publish-osx-arm64

publish-osx-x64:
	$(DOTNET) publish NyxAssetsEditor.csproj -c $(CONFIGURATION) -r osx-x64 --self-contained true -p:PublishSingleFile=true -o $(OUTPUT_DIR)/osx-x64

publish-osx-arm64:
	$(DOTNET) publish NyxAssetsEditor.csproj -c $(CONFIGURATION) -r osx-arm64 --self-contained true -p:PublishSingleFile=true -o $(OUTPUT_DIR)/osx-arm64
