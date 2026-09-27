# live concert plan — phase 1: song list + character selection

written against the umadump extraction only. every datum in this doc cites the
dump file it came from; anything the dump does not answer goes to the open
questions section instead of being guessed. umaviewer 1 was used only to check
where the game itself keeps data, never as authority; where the viewer's
behavior and the dump agree the dump is still the citation.

the full dump layout is being reorganized right now, so citations below use
content-relative paths (the two load-bearing sources are the master database
twin and the decrypted manifest) rather than fragile directory-by-directory
paths.

## what phase 1 builds

a minimal app that does two things:

1. lists the playable live concerts from the game's own data
2. for the chosen concert, presents the characters that can be loaded into it
   and records the selection

no stage, no audio, no timeline. phase 1 is a data browsing surface: it proves
the two catalogs (songs, characters) and the two joins between them (stage
wiring, member rules) read correctly from the dump data, and nothing more.

## source 1 — the song catalog

the complete list of concerts the game data supports is the intersection of
three dump sources, all of which agree:

- `out/song_gaps.json` — 61 keys (song_id, name, seconds, member_count,
  worksheets). generated from the cutt bundle census.
- `out/l3_livesettings_summary.json` — 62 keys; the 62nd is 3180, a menu jingle
  whose cutt bundle reuses son1193's worksheets (see open questions).
- `master.mdb` (twin: `out/master_lz4_decompressed.sqlite`) `live_data` table:
  206 rows total. the 61 census ids are a strict subset. 51 of the 61 have
  `has_live=1`; the ten without the flag are 1052, 1080, 1091, 1095, 1152,
  1153, 1174, 1180, 3193, 9051. the dump documents these as short/insert songs
  and photo-mode jingles (no menu bgm rows, per-chara awb never shipped), so
  the list UI shows all 61 but marks the flag.

per-song row, with the exact source of each column:

| column | source |
|---|---|
| music_id | live_data.music_id == song_gaps.json song_id |
| title | text_data where category=16 and index=music_id (japanese; e.g. 1001 = うまぴょい伝説) |
| display sort | live_data.sort |
| member count | live_data.live_member_number (authoritative; song_gaps member_count is stage slot count, see open questions) |
| default outfit | live_data.default_main_dress (+ default_main_dress_color) |
| backdancer outfit | live_data.backdancer_dress |
| mob outfit | live_data.default_mob_dress |
| stage | livesettings txt row type=1, param1 = stage number -> 3d/env/live/live{param1} |
| seconds | song_gaps.json timeLength_s |
| restrictions | live_dress_restrict_data (2 rows, music 1051/9051 block dress 901011) |
| fixed members | live_fix_member_data (music 1154, 1157 only; pins chara+dress per slot) |
| recommended formation | live_recommend_formation, 414 rows over 37 songs |

the livesettings text files are already extracted as `out/livesettings/<sid>.txt`
(65 files), csv rows `id,type,param1..param5`. the type=1 row gives the stage,
type=0 names the cutt bundle, type=4 rows name the uv movies, type=10 rows the
audience animation clips, type=11 projector textures.

## source 2 — the character catalog

- `chara_data` table: 172 rows, one per character (id, name via text_data
  category=6, height, bust, scale, skin, shape, socks, tail_model_id,
  personal_dress).
- `dress_data`: 513 rows. the character screen cares about:
  - dresses with `use_live=1 or use_live_theater=1`: 482 rows covering all 172
    characters (every character has at least one live-usable outfit)
  - `chara_id` 0 = shared outfits (e.g. 7 = ライブ専用衣装 backdancer outfit,
    101/102/103 = the winning live concert outfits)
  - per-character dresses: id pattern {chara_id}{sub:02d}00, e.g. 100102 =
    special week's second outfit; dress 901001 is a separate 90-series
- `text_data` category=6: character names, category=14: outfit names,
  category=15: outfit descriptions.

the verified bundle naming for a selected character + outfit (checked against
the decrypted manifest, 364,663 rows, and the on-disk decrypted tree):

- character-specific outfit (dress_data.chara_id != 0):
  body dir `3d/chara/body/bdy{chara_id}_{body_type_sub:02d}/` and
  head dir `3d/chara/head/chr{chara_id}_{head_sub_id:02d}/` — verified for all
  449 character-specific live dresses, zero misses. the dir holds one
  `pfb_bdy{...}` prefab plus `tex_*` variants and `clothes/pfb_*_clothNN`
  physics assets.
- shared outfit (chara_id == 0): body dir
  `3d/chara/body/bdy{body_type:04d}_{body_type_sub:02d}/`; inside, prefabs are
  parameterized `pfb_bdy{bt}_{sub}_{setting}_{height}_{shape}_{bust}` using
  chara_data values. 62 of 64 shared dresses have dirs; 48/49 (body_type 16)
  have no body dir — the dump shows them as mini-character-only outfits
  (`mbdy0016_00`), so they are not live-loadable and get filtered out.
- tail: `3d/chara/tail/tail{tail_model_id:04d}_{sub}/` with per-character
  texture suffix (tex_tail0001_00_1001_diff for chara 1001).

## the two joins

### song -> stage

livesettings row type=1 param1 = the stage directory number.
`3d/env/live/live{param1}/pfb_env_live{param1}_controller000.unity3d` is the
stage controller prefab; per-camera variants go controller000..009. four stages
are each shared by two songs (20001->1025+1026, 20005->1051+9051,
10150->1180+3180, 10154->1193+3193); the other 53 stages are one song each.
phase 1 does not load the stage, but the list stores the stage id per song so
later phases never re-derive it.

### song -> member rules

for a chosen song the character picker needs three rules, all from live_data
and the three member tables:

1. slot count = live_data.live_member_number (values in the 61: 1..20, mode 18)
2. per-song allowed characters: live_permission_data (1563 rows, music_id +
   chara_id). coverage is sparse: 50 of the 61 songs have rows, e.g. 1001
   allows only chara 2001, 1004 allows 31. songs with zero permission rows
   (e.g. 1006) are open — see open questions.
3. default fills: when the user does not pick, live_recommend_formation
   (per-slot chara + dress) or live_fix_member_data provides the game's own
   default cast. the recommend table covers 37 songs with one row per slot.

## phase 1 app design

two windows, like the umamusume explorer's flow: a picker window first, then
the concert window.

### window 1 — picker (modeled on the explorer's song -> unit setup flow)

the interaction pattern, not the code, comes from the explorer's
UnitSetupForm/CharacterSelectForm pair (his own repo, read for orientation):

1. song list: the 61 concerts, searchable, showing jacket art
   (`live/jacket/jacket_icon_l_{music_id}`), title, member count, length
2. choose a song -> character grid: `live_member_number` slots laid out like
   stage positions (the explorer mirrors slot i and pivot at characterCount/2)
3. each slot: click to open a character picker filtered by
   live_permission_data for the song (id 0 = mob/audience filler, the explorer
   treats 0 as a valid pick), default filled from live_recommend_formation or
   live_fix_member_data
4. dress per slot: default from live_data (default_main_dress /
   backdancer_dress), overridable per slot from the character's use_live
   outfit list

selection state saves to json next to the app (song id + per-slot
chara/dress) so the concert window and later phases read a stable handoff.

### window 2 — concert window (phase 2 surface, stub in phase 1)

the live concert window the picker launches. phase 1 ships it as an empty
shell bound to the selection json so the handoff is exercised end to end:
window 2 opens, reads the selection, and displays what it loaded (song title,
member list, stage id) instead of rendering. timeline, stage, and audio land
in later phases per the decode backlog.

### module layout

```
assets/Scripts/
  app/            entry, window wiring, selection json io
  data/
    master_db.cs      sqlite reader over master.mdb
    manifest.cs       decrypted-manifest name lookup (name -> bundle file)
    livesettings.cs   csv row model for out/livesettings/<sid>.txt
  concert/
    song_catalog.cs   the 61-row list: id, title, members, stage, seconds, flags
    chara_catalog.cs  172-row character list + outfit filter (use_live)
    song_chara_rules.cs  member count, permission filter, defaults
  ui/
    picker_form.cs     window 1: song list + slot grid + pick popups
    chara_pick_ui.cs   the per-slot character picker popup
    concert_window.cs  window 2 shell: reads selection, renders a summary
```

two windows, five data files, no timeline code. every list cell renders from
catalog objects, nothing hardcoded.


## open questions for the il2 agent

things the dump does not document. none of these block the list+picker, but
each one is a guess if we ship past phase 1 without an answer:

1. `has_live` semantics. 51 of 61 census songs have has_live=1. the ten without
   the flag all have cutt bundles and livesettings rows. what does the game
   itself use has_live for — is it the in-game playable concert flag, an event
   unlock, or a ui-only filter?
2. `live_member_number` vs song_gaps member_count. live_data says 1001 has 18
   members; the cutt census counts 20 stage slots (and 1-member songs like
   1025 show 20 slots too). which number governs the pick ui? we assume
   live_member_number (the game's own column) and treat the 20 as audience/
   stage slot count, but that is an assumption.
3. `live_permission_data` holes. 11 census songs have zero permission rows
   (1006, 1009, 1010, 1012, 1027..1031 range, 1153, 1181, 3193, 9051 — the full
   set is queryable). is no-rows = anyone can be picked, or does the game fall
   back to another table?
4. dress 48/49 (body_type 16) have no full-size body bundle in the manifest,
   only mini-character versions, and dress_data shows costume rows that
   reference them. are these ever live-loadable or always mini-only?
5. the 62nd livesettings entry (3180) — is it a playable concert or a menu
   jingle whose data reuses 1193's? the dump says stage 10150 is shared by
   1180+3180, but nothing states whether the game ever plays 3180 as a concert.
6. chara_data.personal_dress is 0 for chara 1001 but dress_data carries
   100101/100102. what column actually drives the game's default outfit choice
   for a concert member: default_main_dress from live_data, personal_dress, or
   the recommend-formation dress_id?
7. the game's own sqlite access: master.mdb is plain sqlite3, but we need the
   il2 agent's read on which library the game binds (it ships a native sqlite;
   whether our reader must match its expected schema or can read the twin
   file standalone). this is a tooling question, not a data question.

## verification checklist for phase 1

- every song list row: id present in song_gaps.json, live_data, and
  livesettings txt — three-way agree, else the row is flagged not hidden
- titles resolve for all 61 via text_data cat 16 (spot-verified: 1001, 1004,
  1005, 1006)
- every character offered in a picker: dress filter passes use_live,
  bdy/chr bundle dir exists in the decrypted tree (rule verified 449/449 for
  character-specific outfits)
- stage ids resolved for all 61 from livesettings type=1 rows
- no hardcoded ids anywhere in the ui; delete-a-table regression: dropping any
  master.mdb table must break the corresponding surface, not silently fall
  back to a baked list
