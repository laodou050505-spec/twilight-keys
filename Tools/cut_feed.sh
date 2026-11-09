#!/bin/bash
# 棋子收尾合格后自动切底座（Tools/cut_base.py）
cd /Users/ld/Documents/ChatGPT/TwilightKeys
while true; do
  for d in ArtSource/ModelsP2/kd_* ArtSource/ModelsP2/cl_*; do
    n=$(basename $d)
    [ -f $d/final/report.json ] && grep -q '"pass": true' $d/final/report.json || continue
    [ -f $d/final/cut.json ] && continue
    /Applications/Blender.app/Contents/MacOS/Blender -b -P Tools/cut_base.py -- $n 2>&1 | grep " lod" | sed "s/^/$(date +%T) /"
  done
  sleep 60
done
