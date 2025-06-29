<h1 align="center">Jellyfin IMDb Plugin</h1>

## About

Get metadata for movies and other video content from IMDb.

⚠️ **Uses https://github.com/bakeneko/imdb-api** ⚠️

## Build & Installation Process

1. Clone this repository

2. Ensure you have .NET Core SDK setup and installed

3. Build the plugin with following command:

```bash
dotnet publish --configuration Release --output bin
```

4. Place the resulting `Jellyfin.Plugin.IMDb.dll` file in a folder called `plugins/` inside your Jellyfin installation / data directory.

Repository: https://raw.githubusercontent.com/bakeneko/jellyfin-plugin-imdb/main/manifest.json