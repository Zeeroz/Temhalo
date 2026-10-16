"""Render the measured CSV from a completed native run. Development tool only; requires matplotlib."""
import argparse
import csv
import json
from pathlib import Path
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

parser = argparse.ArgumentParser()
parser.add_argument('run_directory', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
report = json.loads((args.run_directory / 'robustness-result.json').read_text(encoding='utf-8-sig'))
assert report['Status'] == 'passed'
with (args.run_directory / 'performance.csv').open(encoding='utf-8-sig', newline='') as source:
    rows = list(csv.DictReader(source))
minutes = [float(row['seconds']) / 60 for row in rows]
buffer = [int(row['logical_buffer_bytes']) / 1048576 for row in rows]
resident = [int(row['working_set_bytes']) / 1048576 for row in rows]
cpu = [float(row['cpu_percent']) for row in rows]
night, surface, ink, muted, mint, lavender = '#0D1218', '#151D26', '#F1F5F9', '#96A7B9', '#7CE8CD', '#B9B2FF'
plt.rcParams.update({'font.family': 'DejaVu Sans', 'font.size': 10, 'text.color': ink,
    'axes.labelcolor': muted, 'xtick.color': muted, 'ytick.color': muted, 'axes.edgecolor': '#2C3C4C'})
figure, axes = plt.subplots(2, 1, figsize=(12, 7), sharex=True, gridspec_kw={'height_ratios': [1.2, 1]})
figure.patch.set_facecolor(night)
for axis in axes:
    axis.set_facecolor(surface)
    axis.grid(alpha=.15, color=muted)
    axis.spines[['top', 'right']].set_visible(False)
    axis.set_xlim(0, max(minutes))
axes[0].plot(minutes, resident, color=lavender, alpha=.8, linewidth=1, label='Working set · hôte et processus du relais')
axes[0].fill_between(minutes, buffer, color=mint, alpha=.14)
axes[0].plot(minutes, buffer, color=mint, linewidth=1.5, label='Budget logique utilisé · buffer partagé')
axes[0].set_ylabel('Mémoire (Mio)')
axes[0].set_ylim(bottom=0)
legend = axes[0].legend(loc='upper right', facecolor=surface, edgecolor='#2C3C4C', labelcolor=ink, fontsize=9)
axes[1].plot(minutes, cpu, color=mint, linewidth=.8)
axes[1].axhline(5, color='#F6C66D', linestyle='--', linewidth=1, label='Objectif CPU : 5 %')
axes[1].set_ylabel('CPU total normalisé (%)')
axes[1].set_xlabel('Temps réel écoulé (minutes)')
axes[1].set_ylim(0, max(5.5, max(cpu) * 1.1))
axes[1].legend(loc='upper right', facecolor=surface, edgecolor='#2C3C4C', labelcolor=ink, fontsize=9)
figure.suptitle('Temhalo · essai local de robustesse', x=.075, ha='left', fontsize=20, fontweight='bold')
figure.text(.075, .915, f"{report['ElapsedSeconds']/60:.1f} min · 4 sorties RTMP · mire {report['SourceMbps']:.2f} Mbit/s · délai initial 300 s", color=muted)
figure.text(.075, .025, f"CPU moyen en émission : {report['CpuMeanPercent']:.2f} % · P95 : {report['CpuP95Percent']:.2f} %\n"
    'Hôte moteur + ingest, lecteur et émetteurs. Mire, récepteur et captures exclus. Mesure locale ; UI et plateformes publiques non qualifiées.', color=muted, fontsize=9)
figure.subplots_adjust(top=.87, bottom=.15, hspace=.18, left=.075, right=.98)
args.output.parent.mkdir(parents=True, exist_ok=True)
figure.savefig(args.output, dpi=160, facecolor=night)
print(args.output.resolve())
