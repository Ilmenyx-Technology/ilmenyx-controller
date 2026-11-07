#!/bin/bash
# Grava a cada segundo, no SSD (fsync), o estado da máquina; se o sistema congelar, as últimas linhas mostram o que subia.
out=${1:-$HOME/ilmenyx-diag/gravacao-$(date +%Y%m%d-%H%M%S).csv}
echo "hora,cpu_temp_c,cpu_uso_pct,cpu_mhz,iowait_pct,ram_livre_mb,swap_usado_mb,gpu_uso_pct,gpu_temp_c,gpu_vram_mb,gpu_mhz,gpu_w,pendrive_le_mb_s,carga1,jogo_rss_mb,top_processo" > "$out"
read -r _ u n s i w x y z _ < /proc/stat; pt=$((u+n+s+i+w+x+y+z)); pi=$i; pw=$w
dev=$(lsblk -no PKNAME "$(findmnt -no SOURCE /media/$USER/Ventoy 2>/dev/null)" 2>/dev/null); pr=$(awk -v d="$dev" '$3==d{print $6}' /proc/diskstats)
while true; do
  sleep 1
  read -r _ u n s i w x y z _ < /proc/stat; t=$((u+n+s+i+w+x+y+z)); dt=$((t-pt)); [ $dt -gt 0 ] || dt=1
  uso=$(( 100*(dt-(i-pi)-(w-pw))/dt )); iow=$(( 100*(w-pw)/dt )); pt=$t; pi=$i; pw=$w
  temp=$(for f in /sys/class/hwmon/hwmon*/; do [ "$(cat $f/name 2>/dev/null)" = coretemp ] && cat $f/temp1_input; done | head -1); temp=$((${temp:-0}/1000))
  mhz=$(awk '/MHz/{s+=$4;n++}END{if(n)printf "%d",s/n}' /proc/cpuinfo)
  ram=$(awk '/MemAvailable/{printf "%d",$2/1024}' /proc/meminfo); sw=$(awk '/SwapTotal/{t=$2}/SwapFree/{f=$2}END{printf "%d",(t-f)/1024}' /proc/meminfo)
  gpu=$(timeout 2 nvidia-smi --query-gpu=utilization.gpu,temperature.gpu,memory.used,clocks.gr,power.draw --format=csv,noheader,nounits 2>/dev/null | tr -d ' ' ); [ -n "$gpu" ] || gpu=",,,,"
  r=$(awk -v d="$dev" '$3==d{print $6}' /proc/diskstats); le=$(( ( ${r:-0} - ${pr:-0} ) * 512 / 1048576 )); pr=$r
  load=$(cut -d' ' -f1 /proc/loadavg)
  jogo=$(pgrep -f '[M]K10[.]exe$' | head -1); rss=$([ -n "$jogo" ] && awk '/VmRSS/{printf "%d",$2/1024}' /proc/$jogo/status 2>/dev/null)
  top=$(ps -eo comm,%cpu --sort=-%cpu --no-headers | head -1 | tr -s ' ' ':')
  echo "$(date +%T),$temp,$uso,$mhz,$iow,$ram,$sw,$gpu,$le,$load,${rss:-},$top" >> "$out"
  sync -f "$out"
done
