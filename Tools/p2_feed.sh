#!/bin/bash
# 自动投料：参考图到了就开 P2（只推进到“等待审批”）；最多同时 3 个 P2 进程。白模审批仍由人工看审计图后执行 approve / reject。
cd /Users/ld/Documents/ChatGPT/TwilightKeys
B=/Applications/Blender.app/Contents/MacOS/Blender
while true; do
  for n in $(python3 -c "import json;print(' '.join(k for k in json.load(open('ArtSource/specs.json')) if not k.startswith(('kd_','cl_'))))"); do
    [ -f "ArtSource/Refs/$n/image.png" ] || continue
    [ -d "ArtSource/ModelsP2/$n" ] && continue
    while [ "$(pgrep -f 'p2_pipeline.py -- run' | wc -l)" -ge 3 ]; do sleep 20; done
    mkdir -p "ArtSource/ModelsP2/$n"
    nohup $B -b -P Tools/p2_pipeline.py -- run "$n" > "/Users/ld/Documents/ChatGPT/TwilightKeys/Logs/p2_$n.log" 2>&1 &
    echo "$(date +%T) 开始 $n"
    sleep 5
  done
  sleep 60
done
