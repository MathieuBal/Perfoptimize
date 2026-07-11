# Perfoptimize

Assistant d'optimisation de jeux : détection du matériel, observation des
performances en jeu, identification du composant limitant et recommandations
de réglages graphiques.

## État du projet — étape 1 : détection matérielle

Cette première étape fournit une application C# (.NET 8) capable de détecter
précisément :

- **CPU** : nom, constructeur, cœurs physiques/logiques, fréquence maximale ;
- **GPU** : nom, constructeur, **VRAM**, version du pilote, carte dédiée ou intégrée ;
- **RAM** : quantité totale, disponible, vitesse (MT/s), nombre de barrettes ;
- **Écran(s)** : nom, résolution, fréquence de rafraîchissement ;
- **Stockage** : modèle, type (SSD/HDD), capacité, espace libre par volume ;
- **Système** : nom et version de l'OS, architecture.

### Utilisation

```bash
dotnet run --project src/Perfoptimize.Cli              # rapport lisible
dotnet run --project src/Perfoptimize.Cli -- --json    # profil JSON sur stdout
dotnet run --project src/Perfoptimize.Cli -- --json hardware-profile.json
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
  `MSFT_PhysicalDisk`, `WmiMonitorID`, …). La VRAM est lue dans le registre
  (`HardwareInformation.qwMemorySize`), car la valeur WMI `AdapterRAM` est
  plafonnée à 4 Go. Aucun code n'est injecté dans les jeux — uniquement des
  lectures système autorisées.
- **Linux** : lecture de `/proc`, `/sys` et des outils standards
  (`lspci`, `xrandr`, `nvidia-smi`) — utile pour le développement et les
  configurations Proton/Steam Deck.

Chaque composant est sondé indépendamment : un capteur inaccessible
(pilote manquant, WMI restreint…) n'empêche jamais la construction du reste
du profil.

### Structure

```text
src/
├── Perfoptimize.Core/          # bibliothèque de détection (réutilisable par la future UI)
│   ├── Hardware/
│   │   ├── HardwareProfile.cs      # modèles du profil matériel
│   │   ├── HardwareScanner.cs      # point d'entrée, choix de la plateforme
│   │   ├── GpuClassifier.cs        # heuristiques dédiée/intégrée, constructeurs
│   │   ├── Windows/                # implémentation WMI + registre
│   │   └── Linux/                  # implémentation /proc, /sys
│   └── Formatting/
└── Perfoptimize.Cli/           # application console
tests/
└── Perfoptimize.Core.Tests/    # tests unitaires (parseurs et heuristiques)
```

### Développement

```bash
dotnet build Perfoptimize.sln
dotnet test Perfoptimize.sln
```

### Prochaines étapes

1. Détection du jeu lancé (surveillance des processus) ;
2. Moniteur de performances (FPS, frametimes, utilisation CPU/GPU/RAM/VRAM, températures) ;
3. Détection du goulot d'étranglement ;
4. Moteur de recommandations + profils de jeux (JSON) ;
5. Interface graphique (WPF/WinUI) et base SQLite.
