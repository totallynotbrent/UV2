#!/usr/bin/env bash
# the no-sidecar gate: the concert must boot with every datapack copy deleted.
set -uo pipefail
export DISPLAY=:99
cd /work/UmaViewer2/Builds
export UV2_MAIN_PATH=/work/data/umamusume_Data/Persistent

# DELETE every sidecar the phase-2 work used to rely on
rm -rf data
rm -f shader_name_map.tsv

python3 - <<'EOF'
import sqlite3, json
conn = sqlite3.connect("/work/data/umamusume_Data/Persistent/master/master.mdb")
cur = conn.cursor()
cur.execute("SELECT position_id, chara_id, dress_id FROM live_recommend_formation WHERE music_id=1004 ORDER BY position_id")
rows = cur.fetchall()
cur.execute("SELECT live_setting FROM live_data WHERE music_id=1004")
stage = cur.fetchone()
sel = {"music_id": 1004, "title": "The Down", "member_count": len(rows), "stage_id": -1,
       "slots": [{"position": p, "chara_id": c, "dress_id": d} for p, c, d in rows]}
open("selection.json", "w").write(json.dumps(sel, indent=2))
print(f"wrote selection: {len(rows)} slots")
conn.close()
EOF

Xvfb :99 -screen 0 1280x720x24 &
XVFB_PID=$!
sleep 2
./UV2 -uv2shot 10 -logFile /work/UmaViewer2/Logs/player_nosidecar.log &
PLAYER_PID=$!
sleep 35
kill $PLAYER_PID 2>/dev/null
kill $XVFB_PID 2>/dev/null
sleep 2
echo '---- no-sidecar markers ----'
grep -aE '\[stage_loader\]|\[worksheet_reader\]|\[slot_sequences\]|\[shader_manager\]|Exception|open failed' /work/UmaViewer2/Logs/player_nosidecar.log | grep -v memorysetup | head -25
echo '---- shots ----'
ls -t e2e_shots/*.png | head -2
echo NOSIDECAR_DONE
