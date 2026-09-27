# UV2

A live concert viewer for Umamusume: Pretty Derby, built from scratch. The
UmaExplorer desktop app is the front end: you pick a song and its cast there,
then the launch button starts this viewer with your selection.

This repository only contains the concert player. It reads the song, stage,
and character data from your own game installation at runtime. Nothing from
the game ships with the exe.

## Requirements

- Windows x64
- Umamusume: Pretty Derby installed (a full, updated install)
- UmaExplorer with the concert launch button, sitting next to UV2.exe or in a
  UV2 folder beside it

## How it works

The explorer writes `selection.json` (song id, stage id, and the cast with
outfits) next to UV2.exe and starts it. The viewer reads that file, resolves
the song and characters from the game database and asset bundles, and opens
the concert window. The stage, audio, and timeline arrive in later phases;
for now the window confirms what it loaded.

`Config.json` is generated on first run and points at the game's
`umamusume_Data/Persistent` folder if the default is wrong.

## Project structure

```
Assets/
  Scripts/
    app/            entry, scene boot, config, selection json io
    data/           sqlite readers (master + encrypted meta), bundle decrypt stream
    concert/        song catalog, character catalog, member rules (later phases)
    ui/             concert window, ui factory
  Editor/           scene baker and batch build entry
  Plugins/          sqlite natives for windows/linux
ProjectSettings/     unity 2022.3.62, IL2CPP standalone
```

## Architecture

```mermaid
flowchart LR
    explorer[UmaExplorer<br/>song + cast selection]
    json[selection.json]
    master[master.mdb<br/>song and character tables]
    meta[meta db<br/>bundle name, file hash, key]
    dat[dat/xx/hash<br/>encrypted asset bundles]
    concert[Concert window]

    explorer --> json --> concert
    master --> concert
    meta --> concert
    dat --> concert
```

## Building

Unity 2022.3.62f2, IL2CPP, Windows x64. CI builds the exe on every push to
`experimental` and on version tags.

## License

All rights reserved. This repository contains no game data; the viewer reads
the user's own game installation at runtime.
