#!/bin/bash
# 已审批的白模继续跑贴图：参数是名称列表；最多同时 3 个 P2 进程
cd /Users/ld/Documents/ChatGPT/TwilightKeys
B=/Applications/Blender.app/Contents/MacOS/Blender
for n in "$@"; do
  while [ "$(pgrep -f 'p2_pipeline.py -- run' | wc -l)" -ge 3 ]; do sleep 20; done
  nohup $B -b -P Tools/p2_pipeline.py -- run "$n" >> "Logs/p2_$n.log" 2>&1 &
  echo "$(date +%T) 继续 $n"; sleep 5
done
