#!/bin/bash
# Repeatable jank measurement on the connected device: Songs list scroll + swipes. Usage: tools/jank.sh [label]
D=$(adb devices | awk 'NR==2{print $1}')
adb -s $D shell am force-stop com.ipodemu
adb -s $D shell dumpsys gfxinfo com.ipodemu reset >/dev/null
adb -s $D shell am start -n com.ipodemu/.ui.MainActivity >/dev/null; sleep 5
adb -s $D shell dumpsys gfxinfo com.ipodemu reset >/dev/null
adb -s $D shell input tap 250 595; sleep 1.5
for i in 1 2 3 4 5 6; do adb -s $D shell input swipe 360 620 360 120 180; sleep 0.25; done
for i in 1 2 3; do adb -s $D shell input swipe 360 150 360 640 180; sleep 0.25; done
adb -s $D shell input swipe 200 400 560 410 200; sleep 1
echo "== ${1:-run}"
adb -s $D shell dumpsys gfxinfo com.ipodemu | grep -E "Total frames|Janky frames:|50th percentile|90th percentile|95th percentile|99th percentile|Slow UI"
