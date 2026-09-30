#!/usr/bin/env bash
# the all-songs acceptance run: boots the headless player once per playable song
# and records a per-song result table (selection, worksheet, stage, cast, motion,
# music) to Logs/all_songs_run.json.
set -uo pipefail
export DISPLAY=:99
cd /work/UmaViewer2/Builds
export UV2_MAIN_PATH=/work/data/umamusume_Data/Persistent

rm -rf data
rm -f shader_name_map.tsv

python3 - <<'EOF'
import sqlite3, json
conn = sqlite3.connect("/work/data/umamusume_Data/Persistent/master/master.mdb")
cur = conn.cursor()
cur.execute("SELECT music_id FROM live_data WHERE has_live=1 ORDER BY music_id")
songs = [r[0] for r in cur.fetchall()]
out = []
for song in songs:
    cur.execute("SELECT position_id, chara_id, dress_id FROM live_recommend_formation WHERE music_id=? ORDER BY position_id", (song,))
    rows = cur.fetchall()
    if not rows:
        # mirror the launcher: allowed charas round-robin + the song's default dress
        cur.execute("SELECT chara_id FROM live_permission_data WHERE music_id=? ORDER BY chara_id", (song,))
        allowed = [r[0] for r in cur.fetchall()]
        cur.execute("SELECT live_member_number, default_main_dress, backdancer_dress FROM live_data WHERE music_id=?", (song,))
        count, main_dress, back_dress = cur.fetchone()
        cur.execute("SELECT id, chara_id FROM dress_data WHERE costume_type=0 ORDER BY id")
        live_dresses = cur.fetchall()
        rows = []
        for pos in range(count or 9):
            chara = allowed[pos % len(allowed)] if allowed else 0
            dress = main_dress if (chara and main_dress) else back_dress
            if not any(d[0] == dress for d in live_dresses):
                dress = next((d[0] for d in live_dresses if d[1] == chara), back_dress)
            rows.append((pos + 1, chara, dress))
    out.append({"music_id": song, "member_count": len(rows),
                "slots": [{"position": p, "chara_id": c, "dress_id": d} for p, c, d in rows]})
json.dump(out, open("/tmp/uv2_all_songs.json", "w"))
print(f"{len(out)} songs")
conn.close()
EOF

Xvfb :99 -screen 0 1280x720x24 &
XVFB_PID=$!
sleep 2

python3 - <<'EOF'
import json, subprocess, time, os, re

songs = json.load(open("/tmp/uv2_all_songs.json"))
results = []
trace_path = "/work/UmaViewer2/Builds/uv2_trace.log"

for i, song in enumerate(songs):
    sel = {"music_id": song["music_id"], "title": f"song {song['music_id']}",
           "member_count": song["member_count"], "stage_id": -1, "slots": song["slots"]}
    json.dump(sel, open("/work/UmaViewer2/Builds/selection.json", "w"))
    if os.path.exists(trace_path):
        os.remove(trace_path)

    proc = subprocess.Popen(["./UV2", "-uv2shot", "6", "-logFile",
                             "/work/UmaViewer2/Logs/player_all.log"],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    deadline = time.time() + 45
    ok = False
    while time.time() < deadline:
        time.sleep(3)
        if proc.poll() is not None:
            break
        if os.path.exists(trace_path):
            t = open(trace_path, errors="ignore").read()
            if "concert open: SUCCESS" in t:
                ok = True
                break
            if "concert open: FAILED" in t:
                break
    proc.kill()
    proc.wait()

    row = {"music_id": song["music_id"], "members": song["member_count"], "opened": ok}
    if os.path.exists(trace_path):
        t = open(trace_path, errors="ignore").read()
        m = re.search(r"worksheet bound: (\d+) cam keys", t)
        row["cam_keys"] = int(m.group(1)) if m else 0
        m = re.search(r"stage geometry: (\d+) roots, (\d+) renderers", t)
        row["stage_roots"] = int(m.group(1)) if m else 0
        m = re.search(r"cast: (\d+) loaded, (\d+) missed", t)
        row["cast"] = int(m.group(1)) if m else 0
        row["cast_missed"] = int(m.group(2)) if m else -1
        m = re.search(r"motion: (\d+) authored names -> (\d+) clips loaded", t)
        row["clips"] = int(m.group(2)) if m else 0
        m = re.search(r"music: oke playing \((\d+)Hz, ([\d.]+)s", t)
        row["music"] = bool(m)
        m = re.search(r"beat t=[\d.]+s .*renderers (\d+)/(\d+) visible", t)
        row["renderers_visible"] = int(m.group(1)) if m else 0
        m = re.search(r"concert open: FAILED - (.+)", t)
        row["error"] = m.group(1)[:80] if m else ""
    results.append(row)
    print(f"[{i+1}/{len(songs)}] {song['music_id']}: {'OK' if ok else 'FAIL'} "
          f"cast={row.get('cast',0)}/{song['member_count']} stage={row.get('stage_roots',0)} "
          f"clips={row.get('clips',0)} music={row.get('music',False)} {row.get('error','')}", flush=True)

json.dump(results, open("/work/UmaViewer2/Logs/all_songs_run.json", "w"), indent=1)
fails = [r for r in results if not r["opened"]]
print(f"TOTAL: {len(results)} songs, {len(results)-len(fails)} opened, {len(fails)} failed")
EOF

RC=$?
kill $XVFB_PID 2>/dev/null
exit $RC
