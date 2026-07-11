# Perfoptimize

Assistant d'optimisation de jeux : détection du matériel, observation des
performances en jeu, identification du composant limitant et recommandations
de réglages graphiques.

## État du projet

- ✅ **Étape 1 — détection matérielle** (CPU, GPU, VRAM, RAM, écrans, stockage, OS)
- ✅ **Étape 2 — détection du jeu lancé** (profils de jeux JSON + surveillance des processus)
- ✅ **Étape 3 — moniteur de performances** (CPU, GPU, RAM, VRAM, températures)
- ✅ **Étape 4 — goulot d'étranglement et recommandations** (premières règles)

### Étape 1 : détection matérielle

L'application détecte précisément :

- **CPU** : nom, constructeur, cœurs physiques/logiques, fréquence maximale ;
- **GPU** : nom, constructeur, **VRAM**, version du pilote, carte dédiée ou intégrée ;
- **RAM** : quantité totale, disponible, vitesse (MT/s), nombre de barrettes ;
- **Écran(s)** : nom, résolution, fréquence de rafraîchissement ;
- **Stockage** : modèle, type (SSD/HDD), capacité, espace libre par volume ;
- **Système** : nom et version de l'OS, architecture.

### Utilisation

```bash
dotnet run --project src/Perfoptimize.Cli                      # rapport matériel lisible
dotnet run --project src/Perfoptimize.Cli -- scan --json       # profil JSON sur stdout
dotnet run --project src/Perfoptimize.Cli -- scan --json hardware-profile.json
dotnet run --project src/Perfoptimize.Cli -- games             # profils de jeux connus + jeu en cours
dotnet run --project src/Perfoptimize.Cli -- monitor -d 30     # session d'analyse de 30 s
```

Exemple de session `monitor` :

```text
Detected game: Cyberpunk 2077 (pid 12345)
Sampling for 30s (1 sample/s)... play normally during the capture.

  cpu    54% | gpu    97% | ram    62% | vram    71%
  ...

=== Summary ===
CPU     : avg 55%, peak 61%
GPU     : avg 97%, peak 100%
...

Bottleneck: Gpu
The GPU averaged 97% utilization — rendering load is the limiting factor.

Recommendations:
- Cyberpunk 2077: lower "Ray Tracing" by one step
    Typical gain 5/5 for a visual cost of 3/5.
- Enable upscaling (DLSS / FSR / XeSS) in Quality mode
    Renders at a lower internal resolution for a large GPU-side gain...
```

Exemple de sortie :

```text
=== Perfoptimize — Hardware profile ===

CPU     : AMD Ryzen 5 5600X 6-Core Processor
          6 cores / 12 threads, up to 3.7 GHz
GPU     : NVIDIA GeForce RTX 3060 (discrete, 12 GiB VRAM, driver 31.0.15.3623)
RAM     : 16 GiB (9.2 GiB available, 3200 MT/s, 2 module(s))
Display : DELL S2721DGF — 2560×1440 @ 144 Hz (primary)
Storage : Samsung SSD 980 1TB (SSD, 931.5 GiB)
Volume  : C:\ — 300 GiB free of 931 GiB
OS      : Microsoft Windows 11 Pro (10.0.22631, X64)

Gaming GPU: NVIDIA GeForce RTX 3060
```

### Comment ça marche

- **Windows** : requêtes WMI (`Win32_Processor`, `Win32_VideoController`,
  `MSFT_PhysicalDisk`, `WmiMonitorID`, compteurs de performances GPU de
  l'Observateur de tâches…). La VRAM est lue dans le registre
  (`HardwareInformation.qwMemorySize`), car la valeur WMI `AdapterRAM` est
  plafonnée à 4 Go.
- **Linux** : lecture de `/proc`, `/sys` et des outils standards
  (`lspci`, `xrandr`, `nvidia-smi`) — utile pour le développement et les
  configurations Proton/Steam Deck.

Principes de conception :

- **Aucune injection de code dans les jeux** : uniquement la liste des
  processus et des lectures système autorisées — rien qui puisse inquiéter
  un anti-cheat.
- **Chaque capteur est sondé indépendamment** : un capteur inaccessible
  (pilote manquant, WMI restreint…) n'empêche jamais le reste de l'analyse.
- **Toutes les mesures sont nullables** : « capteur absent » n'est jamais
  confondu avec « valeur zéro », et les règles de décision en tiennent compte.

### Profils de jeux

Chaque jeu connu est décrit par un JSON embarqué
(`src/Perfoptimize.Core/Games/Profiles/`) : ses exécutables et l'impact
habituel de chaque réglage (`gpu`, `cpu`, `vram`, `ram`, `mixed`) avec un coût
visuel et un gain de performance de 0 à 5. Le moteur de recommandations classe
les réglages par efficacité (gain − coût visuel) selon le goulot détecté.
Jeux fournis : Cyberpunk 2077, Elden Ring, GTA V, Path of Exile, The Witcher 3.

### Structure

```text
src/
├── Perfoptimize.Core/          # bibliothèque (réutilisable par la future UI)
│   ├── Hardware/                   # étape 1 : profil matériel
│   │   ├── HardwareProfile.cs      # modèles
│   │   ├── HardwareScanner.cs      # point d'entrée, choix de la plateforme
│   │   ├── GpuClassifier.cs        # heuristiques dédiée/intégrée
│   │   ├── Windows/                # WMI + registre
│   │   └── Linux/                  # /proc, /sys, lspci, xrandr
│   ├── Games/                      # étape 2 : profils et détection du jeu lancé
│   │   ├── GameProfile.cs
│   │   ├── GameProfileRepository.cs
│   │   ├── RunningGameDetector.cs
│   │   └── Profiles/*.json
│   ├── Monitoring/                 # étape 3 : télémétrie système
│   │   ├── TelemetrySample.cs
│   │   ├── PerformanceMonitor.cs
│   │   ├── PerformanceSummary.cs
│   │   ├── Windows/                # compteurs WMI (GPU Engine, GPU Adapter Memory)
│   │   └── Linux/                  # /proc/stat, hwmon, nvidia-smi
│   ├── Optimization/               # étape 4 : analyse et recommandations
│   │   ├── BottleneckDetector.cs
│   │   └── RecommendationEngine.cs
│   └── Formatting/
└── Perfoptimize.Cli/           # application console (scan, games, monitor)
tests/
└── Perfoptimize.Core.Tests/    # tests unitaires (parseurs, règles, heuristiques)
```

### Développement

```bash
dotnet build Perfoptimize.sln
dotnet test Perfoptimize.sln
```

### Prochaines étapes

1. Mesure des FPS et des frametimes (via PresentMon/ETW, sans injection) ;
2. Historique des sessions dans une base SQLite ;
3. Lecture des fichiers de configuration des jeux (avec sauvegarde préalable) ;
4. Objectifs utilisateur (qualité / équilibré / performances) ;
5. Interface graphique (WPF/WinUI).
