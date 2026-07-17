# Perfoptimize

Assistant d'optimisation de jeux et de surveillance du PC : détection du
matériel, mesure des performances en jeu (FPS réels, goulot d'étranglement),
recommandations et application des réglages graphiques — plus un **cockpit
sombre** avec analyse continue temps réel (CPU/GPU/RAM/températures/latence
pilote), diagnostic des crashs et freezes, analyse réseau, et un guide qui
explique ce que change chaque réglage. Moteur partagé entre la CLI et l'UI.

## État du projet

- ✅ **Étape 1 — détection matérielle** (CPU, GPU, VRAM, RAM, écrans, stockage, OS)
- ✅ **Étape 2 — détection du jeu lancé** (profils de jeux JSON + surveillance des processus)
- ✅ **Étape 3 — moniteur de performances** (CPU, GPU, RAM, VRAM, températures)
- ✅ **Étape 4 — goulot d'étranglement et recommandations** (premières règles)
- ✅ **Étape 5 — FPS et frametimes** (analyse de captures PresentMon : FPS moyens, 1% low, à-coups)
- ✅ **Étape 6 — objectifs utilisateur** (qualité / équilibré / performances)
- ✅ **Étape 7 — historique des sessions** (base SQLite locale)
- ✅ **Étape 8 — fichiers de configuration** (localisation, sauvegarde, restauration)
- ✅ **Étape 9 — application des réglages** (prévisualisation par défaut, `--write` explicite)
- ✅ **Étape 10 — conseiller contextuel** (objectif FPS, matériel réel, spécificités du jeu)
- ✅ **Étape 11 — pipeline de mesure v2** (attribution au processus du jeu, charge par cœur,
  throttling confirmé par le pilote, PresentMon intégré à `monitor`)
- ✅ **Étape 12 — diagnostic v3 et boucle mesurée** (attribution utilisée dans les verdicts,
  contention GPU externe, confiance, plan de session appliqué à l'identique, `tune`)
- ✅ **Étape 13 — application de bureau** (interface graphique locale, exécutable Windows
  autonome, téléchargement de PresentMon depuis l'interface)
- ✅ **Étape 14 — détection automatique des jeux + optimisation PC globale** (reconnaissance
  heuristique des jeux sans profil, interface en deux onglets Jeux / Système, conseiller matériel)
- ✅ **Étape 15 — sémantique des réglages** (chaque réglage porte une *action* et un *niveau de
  preuve* ; fin de la logique « tout baisser », gains affichés comme théoriques tant que non mesurés)
- ✅ **Étape 16 — découverte des jeux installés + lecture des configs** (parcours des bibliothèques
  Steam, localisation et lecture générique des fichiers de configuration, sans profil requis)
- ✅ **Étape 17 — scan multi-disques, Epic, et réglages à éviter** (toutes les bibliothèques Steam
  de tous les disques + Epic Games ; base de connaissances communautaire des réglages problématiques)
- ✅ **Étape 18 — refonte cockpit** (interface sombre instrumentée à 9 sections en SPA vanilla,
  jauges circulaires, courbe de frametime réelle ; remplace les deux onglets Jeux / Système)
- ✅ **Étape 19 — analyse continue en arrière-plan** (service `BackgroundService` qui échantillonne
  la machine en permanence côté serveur, indépendamment de la vue affichée)
- ✅ **Étape 20 — moniteur temps réel** (CPU/GPU/RAM + **températures** + **latence DPC/ISR** en
  direct, attribution des ressources par processus vis-à-vis du jeu)
- ✅ **Étape 21 — alertes & notifications** (suivi d'alertes avec identités stables, pastille
  globale, notifications navigateur : surchauffe, processus parasite, latence pilote)
- ✅ **Étape 22 — diagnostic d'incidents** (lecture du journal d'événements Windows : crash appli,
  écran bleu, plantage GPU/TDR, arrêt inattendu, mémoire, disque — avec cause + solution)
- ✅ **Étape 23 — analyse réseau** (inventaire TCP + **attribution des connexions au programme**
  qui les ouvre, résolution des noms, niveau de confiance, guide « normal vs suspect »)
- ✅ **Étape 24 — guide des réglages** (explication en clair de chaque réglage + choix
  **DirectX 11 / 12 / Vulkan** adapté au goulot mesuré)
- ✅ **Étape 25 — FPS live + détection automatique des changements** (le moniteur mesure le FPS en
  continu et signale de lui-même « 48 → 56 FPS (+16 %) » quand un réglage agit, sans re-mesurer)

## Réglages à éviter (connaissance communautaire)

Certains réglages sont mal optimisés : la communauté sait qu'il vaut mieux ne
pas les activer (crashs, saccades, coût disproportionné). Perfoptimize porte
désormais cette connaissance dans les profils, sous forme d'**avisories**
affichées **spontanément** à chaque analyse, quel que soit le goulot :

- Witcher 3 : *HairWorks* (peu rentable), *ray tracing next-gen* (saccades de traversée) ;
- Cyberpunk 2077 : *réflexions Psycho* (peu rentable), *densité de foule élevée* (à-coups CPU) ;
- Elden Ring : *ray tracing* (saccades, jeu déjà plafonné à 60) ;
- GTA V : *MSAA* en 1440p/4K (peu rentable), *distance d'affichage étendue* (à-coups).

Chaque avisory porte un **type** (crash / saccades / peu rentable), une action
recommandée, une explication et un niveau de preuve (`communityValidated`).

## Scan multi-disques

Le scan des jeux installés couvre maintenant **tous les disques fixes** : il
sonde chaque lecteur pour les bibliothèques Steam (`SteamLibrary`, `Steam`,
`Games\SteamLibrary`…), en plus de la racine Steam du registre, et ajoute les
jeux **Epic Games** (manifestes `.item`). Un jeu installé sur D:/K:/… est donc
détecté même s'il n'est pas dans la bibliothèque Steam par défaut.

## Découverte des jeux installés et de leurs configs

Au-delà des 5 profils détaillés, Perfoptimize peut **parcourir le disque** pour
découvrir les jeux installés et lire leurs fichiers de configuration :

- **`InstalledGameScanner`** lit les bibliothèques Steam (`libraryfolders.vdf`)
  et les manifestes d'application (`appmanifest_*.acf`) pour lister les jeux
  réellement installés, avec leur dossier — sur toutes les bibliothèques
  (racine Steam découverte via le registre sous Windows) ;
- **`GameConfigScanner`** cherche les fichiers de config dans le dossier
  d'installation (parcours borné en profondeur et en nombre) **et** dans les
  emplacements utilisateur habituels (`Documents\My Games`, `%LOCALAPPDATA%`,
  `%APPDATA%`, `%LOCALAPPDATA%Low`), en associant les dossiers au jeu par
  correspondance de nom, puis **extrait les réglages graphiques** des fichiers
  INI (ombres, textures, résolution, DLSS/FSR, distance d'affichage…).

Dans l'onglet **Jeux**, le bouton « Scanner mes jeux installés » liste les jeux
découverts ; « Voir la config » affiche les fichiers trouvés et leurs réglages
graphiques actuels. Les formats JSON/XML sont localisés mais pas encore
interprétés. La lecture est strictement bornée aux jeux réellement découverts.

## Sémantique des réglages : ne pas « tout baisser »

Tous les réglages ne s'optimisent pas en les baissant. Chaque `GameSetting`
porte désormais :

- une **action** : `lower` (baisser d'un cran), `disable` (désactiver, ex. ray
  tracing), **`keepEnabled`** (à laisser activé — ex. *Engine Multithreading*
  de Path of Exile, dont la désactivation nuit souvent aux perfs),
  `testAlternatives` (à départager par un test A/B mesuré, ex. renderer
  DX11/DX12/Vulkan), `manualOnly` (à changer dans le menu du jeu) ;
- un **niveau de preuve** : `estimated` (estimation du profil, non vérifiée),
  `communityValidated`, `benchmarked`, `locallyMeasured` (mesuré sur ta
  machine via la boucle `tune`).

Conséquences : un réglage `keepEnabled` n'est **jamais** proposé à la baisse ;
si un jeu connu n'a aucun réglage sûr pour le goulot, l'assistant le **dit**
plutôt que d'inventer des conseils génériques (Path of Exile n'a ni foule ni
distance d'affichage) ; et tant qu'aucune mesure PresentMon n'existe, les gains
sont affichés comme **théoriques** (« jusqu'à ~X % selon le profil »), pas comme
un résultat personnalisé. La confiance du diagnostic (`suspecté` vs `confirmé`)
est mise en avant dans l'interface.

## Détection automatique et optimisation globale

Deux piliers complémentaires — aujourd'hui répartis dans les sections du cockpit
(Analyse/Jeux installés d'un côté, Système/Matériel de l'autre) :

- **Jeux** : le jeu en cours est détecté même s'il n'a **pas de profil détaillé** —
  une heuristique reconnaît un jeu à son emplacement d'installation (dossiers
  Steam, Epic, GOG, Ubisoft, EA, Riot, Battle.net…), en excluant lanceurs,
  navigateurs et outils. Un jeu inconnu reste **analysable** (mesure attribuée
  à son processus) et reçoit des conseils génériques et adaptés au matériel ;
  seuls les jeux avec profil permettent l'application automatique des réglages.
- **Système** : au-delà d'un jeu précis, un **conseiller matériel**
  (`SystemAdvisor`) analyse le PC et signale les optimisations globales — RAM
  en simple canal, 16 Go à l'étroit, profil XMP/EXPO à vérifier, disque système
  presque plein, absence de SSD, pas de carte dédiée — classées par gravité
  (à corriger / conseillé / info).

## Application de bureau (le plus simple)

Pour un usage sans ligne de commande, Perfoptimize se compile en **un seul
exécutable Windows autonome** — le runtime .NET est inclus, rien d'autre à
installer.

```powershell
# Depuis le dossier du projet, sur Windows :
powershell -ExecutionPolicy Bypass -File scripts\build-windows-exe.ps1
# → produit dist\Perfoptimize.exe
```

L'exécutable est produit dans **`dist\Perfoptimize.exe`**. Double-clique
dessus : il démarre un petit serveur local et ouvre un tableau de bord dans ton
navigateur (garde la fenêtre ouverte pendant l'utilisation ; le bouton
« Quitter » l'arrête). L'interface est un **cockpit sombre à 9 sections** (SPA
vanilla, sans framework) :

1. **Vue d'ensemble** — jeu détecté, jauges clés, résumé matériel, dernier diagnostic ;
2. **Analyse** — lancer une mesure (durée, objectif, FPS visés), lire le goulot,
   les jauges, la **courbe de frametime**, les réglages à éviter, les
   recommandations, appliquer/restaurer la config, **et comprendre les réglages**
   (guide DirectX 11/12/Vulkan + glossaire, cf. plus bas) ;
3. **Moniteur** — analyse continue temps réel (cf. plus bas) ;
4. **Jeux installés** — scan multi-disques Steam/Epic + lecture des configs ;
5. **Matériel** — CPU/GPU/RAM/écran/stockage/OS, chaque capteur nullable ;
6. **Système** — conseiller matériel classé par gravité ;
7. **Incidents** — d'où viennent les crashs/écrans bleus/plantages GPU (cf. plus bas) ;
8. **Réseau** — qui parle à Internet, et est-ce normal (cf. plus bas) ;
9. **Outils** — catalogue d'outils décrits (PresentMon installable ; CapFrameX,
   LatencyMon, HWiNFO, GPU-Z, DDU documentés) + sauvegardes de config.

Une **pastille d'alerte** dans la barre supérieure (visible depuis toutes les
sections) et des **notifications navigateur** signalent en direct une surchauffe,
un processus parasite, une latence pilote ou un gain/perte de FPS.

La logique métier est entièrement partagée avec la CLI (projet
`Perfoptimize.Core`) : l'interface n'est qu'une façade, le moteur est le même.
Pour développer l'UI sans produire l'exe : `dotnet run --project src/Perfoptimize.App`.

### Analyse continue en arrière-plan (le Moniteur)

Un service `BackgroundService` échantillonne la machine **toutes les ~2 s côté
serveur**, indépendamment de la section affichée : tant que l'application tourne,
elle « comprend le PC à tout instant ». Le Moniteur affiche en direct :

- **CPU système, GPU, RAM** (jauges), **températures CPU/GPU** et **latence
  DPC/ISR** (le signal de LatencyMon : la cause classique des micro-freezes sans
  saturation CPU/GPU) ;
- le **top des processus** par CPU et par mémoire, avec le jeu mis en avant, et
  une **attribution** en clair (« *chrome* consomme 34 % du CPU — il se dispute le
  processeur avec ton jeu ») ;
- le **FPS en direct** (via PresentMon) et surtout une **détection automatique de
  changement** : quand tu modifies un réglage dans le jeu, le moniteur signale de
  lui-même « 48 → 56 FPS (+16 %) » — sans qu'aucune mesure ne soit relancée. Le
  message est transitoire (il disparaît quand le nouveau niveau devient la norme)
  et déclenche une notification, visible même en plein jeu.

> Le FPS live et les températures dépendent de PresentMon / des capteurs sous
> Windows ; à défaut, l'affichage bascule sur un **état vide honnête**
> (« capteur absent », jamais « 0 »).

### Incidents : d'où viennent les crashs et freezes

La section **Incidents** lit l'historique du journal d'événements Windows (14
derniers jours) — en **lecture seule** — et explique chaque incident de
stabilité avec sa **cause probable** et une **solution concrète** orientée jeu :

- crash d'application, application figée, **plantage du pilote GPU (TDR)**,
  **écran bleu (BSOD)**, arrêt inattendu (Kernel-Power 41), mémoire saturée,
  erreur disque.

### Réseau : qui parle à Internet, et est-ce normal ?

La section **Réseau** rattache chaque connexion sortante au **programme qui
l'ouvre** (`netstat -ano` sous Windows, `ss -tnp` sous Linux) et résout la
destination en nom (reverse DNS) — pour distinguer le normal (navigateur,
Discord, Steam, Windows Update) de l'inhabituel. Les connexions sont triées du
plus suspect au plus banal (**identifié / inconnu / à vérifier** — un exécutable
lancé depuis un dossier temporaire est signalé), avec un guide « comment savoir
si c'est normal ». Note honnête intégrée : ce panneau aide à **voir et
comprendre**, il ne remplace pas un antivirus/Malwarebytes pour un verdict.

### Comprendre les réglages (guide DirectX 11 / 12 / Vulkan)

En bas de la section **Analyse**, un guide explique **en clair ce que change
chaque réglage** (résolution/upscaling, ray tracing, ombres, textures, LOD,
densité de foule, occlusion ambiante, V-Sync, cap FPS, post-traitement…) — ce
qu'il modifie à l'écran, quel composant il coûte (GPU/CPU/VRAM), et quand le
baisser. Le choix de l'**API de rendu (DirectX 11 / 12 / Vulkan)** est expliqué
et **recommandé selon le goulot mesuré** (CPU limité → DX12/Vulkan qui
répartissent mieux sur les cœurs ; plantages → DX11 ; etc.).

### API HTTP locale

L'interface parle à un petit serveur local (loopback uniquement, port
éphémère). Les endpoints, tous en lecture seule sauf `apply`/`restore`/`tools`/
`shutdown` :

| Endpoint | Rôle |
|---|---|
| `GET /api/hardware` | profil matériel |
| `GET /api/games` | jeux connus + jeu en cours |
| `GET /api/system` | conseils matériels (SystemAdvisor) |
| `GET /api/library` · `GET /api/library/config` | jeux installés + lecture des configs |
| `POST /api/monitor` | mesure (durée, objectif, FPS visés) → goulot, métriques, reco |
| `GET /api/live` | instantané continu : CPU/GPU/RAM/temp/DPC, FPS, alertes, changement FPS |
| `GET /api/incidents` | incidents de stabilité expliqués (Windows) |
| `GET /api/network` | inventaire TCP + connexions attribuées par programme |
| `GET /api/settings-guide` | guide des réglages + reco d'API selon le goulot |
| `POST /api/apply` · `POST /api/restore` | prévisualiser/appliquer/restaurer la config |
| `GET /api/tools` · `POST /api/tools/presentmon` | état des outils + install PresentMon |
| `POST /api/shutdown` | arrêt propre depuis le bouton « Quitter » |

### Mettre à jour

Comme le projet se construit depuis un clone git, une mise à jour récupère le
code puis reconstruit l'exe (ferme d'abord l'application) :

```powershell
powershell -ExecutionPolicy Bypass -File scripts\update.ps1
```

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
dotnet run --project src/Perfoptimize.Cli -- monitor --goal quality      # ou balanced, performance
dotnet run --project src/Perfoptimize.Cli -- frametimes capture.csv      # analyse d'une capture PresentMon
dotnet run --project src/Perfoptimize.Cli -- history           # dernières sessions enregistrées
dotnet run --project src/Perfoptimize.Cli -- config witcher              # localiser les fichiers de config
dotnet run --project src/Perfoptimize.Cli -- config witcher --backup     # sauvegarde horodatée
dotnet run --project src/Perfoptimize.Cli -- config witcher --restore    # restaurer (jeu fermé uniquement)
dotnet run --project src/Perfoptimize.Cli -- apply witcher               # prévisualiser les modifications
dotnet run --project src/Perfoptimize.Cli -- apply witcher --session 3 --write   # appliquer le plan de la session 3
dotnet run --project src/Perfoptimize.Cli -- tune witcher                # boucle mesurée : 1 changement, vérifié
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

### FPS et frametimes

La mesure des FPS réels se fait sans injection via
[PresentMon](https://github.com/GameTechDev/PresentMon) (Intel) : on capture un
CSV pendant la session de jeu, puis `perfoptimize frametimes capture.csv`
calcule FPS moyens, **1% low**, pire frametime et pourcentage d'à-coups
(frames ≥ 2 × la médiane). Un jeu à 70 FPS avec de gros à-coups est moins
agréable qu'un jeu stable à 60 FPS — c'est précisément ce que ces métriques
révèlent. Les formats CSV de PresentMon v1 (`MsBetweenPresents`) et v2
(`FrameTime`) sont pris en charge, avec filtre `--process jeu.exe`.

### Objectifs utilisateur

`monitor --goal quality|balanced|performance` change le classement des
recommandations : en mode *quality*, les réglages au coût visuel élevé sont
exclus ; en mode *performance*, davantage de réglages sont proposés et
l'upscaling passe en mode Équilibré/Performance.

### Pipeline de mesure v2 : mesurer le jeu, pas la machine

Un diagnostic n'a de valeur que si les mesures correspondent au jeu. Le
pipeline attribue donc les mesures au processus détecté :

- **GPU par PID et par adaptateur** (Windows) : les compteurs « GPU Engine »
  sont décodés (`pid_1234_luid_0x..._engtype_3D`) pour isoler l'utilisation
  **du jeu** et l'adaptateur (LUID) sur lequel il rend réellement — un iGPU
  de portable, un navigateur ou un overlay ne polluent plus les chiffres, et
  la VRAM est comptée sur cette carte-là uniquement.
- **CPU par cœur et par processus** : en plus du total machine, le pipeline
  suit le cœur le plus chargé et la part CPU du processus du jeu. Le cas
  classique « thread principal à 100 %, CPU total à 35 % » est maintenant
  diagnostiqué comme limite mono-thread au lieu de « aucun goulot ».
- **Throttling confirmé, pas supposé** : `nvidia-smi` fournit l'horloge GPU et
  les raisons de limitation du pilote (`clocks_throttle_reasons`). Un
  ralentissement thermique rapporté par le pilote est annoncé comme
  **confirmé** ; une simple température élevée est annoncée comme une
  **suspicion, explicitement non confirmée** (un power cap normal n'est pas
  du throttling thermique).
- **PresentMon intégré** : sous Windows, si PresentMon est installé (PATH ou
  `PERFOPTIMIZE_PRESENTMON`), `monitor` capture automatiquement les vrais
  frametimes du jeu pendant la session — plus besoin de la manœuvre manuelle
  en deux commandes.

### Diagnostic v3 : des verdicts qui disent leur degré de certitude

- **L'attribution sert au verdict, pas seulement à l'affichage** : jeu ≥ 90 %
  du GPU → limité GPU *confirmé* ; adaptateur saturé mais jeu ≤ 70 % →
  **`ExternalGpuLoad`** (navigateur, OBS…) ; attribution absente → verdict
  *suspecté*, dit comme tel. Côté CPU, l'attribution s'applique au cœur saturé
  **et** à la saturation globale : si la charge n'appartient pas au jeu, le
  verdict est **`ExternalCpuLoad`**. Ces verdicts externes — comme
  **`GpuHardwareLimit`** — ne produisent **jamais** de modification de
  réglages du jeu : fermer le programme responsable, vérifier
  câbles/alimentation, re-analyser.
- **Causes de ralentissement séparées** : bits thermiques du pilote
  (`SwThermalSlowdown`, `HwThermalSlowdown`) → throttling **confirmé** ;
  `HwSlowdown`/`HwPowerBrake` → **`GpuHardwareLimit`** (limite carte ou
  alimentation, pas un problème de réglages) ; température seule → suspicion
  explicite.
- **Gains affichés en fourchette** (`+25 % à +46 %`) : les gains estimés
  interagissent et ne s'additionnent pas linéairement — le plan le dit et
  invite à re-mesurer.
- **Sélection à coût visuel minimal** : pour combler +5 %, le moteur choisit
  le brouillard volumétrique (gain 8 %, coût visuel 1) plutôt que le ray
  tracing (gain 30 %, coût 3) — recherche du sous-ensemble couvrant l'écart
  avec le minimum de perte visuelle.
- **Frame Generation refusée sous ~45 FPS réels** (latence et artefacts), et
  capacité FSR-FG restreinte aux cartes réellement modernes (RTX, RX 5000+,
  Arc). L'objectif FPS est aussi plafonné à la fréquence de l'écran.

### Boucle d'optimisation mesurée (`tune`)

Le cœur du produit : ne plus estimer, **vérifier**. Le workflow réel est en
deux phases, pour que la seconde capture soit forcément produite *après* le
changement :

```text
perfoptimize tune prepare witcher --baseline scene.csv
→ vérifie que le jeu est fermé, applique UN changement (HairWorks 2 → 1),
  sauvegarde, et crée l'expérience 20260711-193106

(l'utilisateur relance le jeu, rejoue la même scène, capture ~30 s)

perfoptimize tune finish 20260711-193106 --after capture.csv
→ Before 48,1 FPS / After 56,2 FPS : +16,8 % mesuré → conservé
  (ou +0,8 % → restauration automatique depuis la sauvegarde exacte)
```

Règles de conservation : gain moyen ≥ 3 % **ou** 1% low ≥ +8 %, mais toute
régression sérieuse est bloquante — moyenne < −1 %, **1% low < −5 % ou stutter
en hausse** font restaurer même si la moyenne progresse. Une seconde mesure
impossible (fichier illisible, exception, annulation) = restauration
systématique, garantie par `try/finally`. La restauration est **chirurgicale** :
seule la clé testée revient à sa valeur d'origine, les modifications que le jeu
a lui-même écrites entre-temps (résolution choisie au menu…) survivent — la
copie de la sauvegarde exacte de l'expérience ne sert que de repli. `prepare`
est **transactionnel** : si l'expérience ne peut pas être persistée, le
changement est immédiatement défait. Et le tuning est **refusé** pour les
verdicts non adressables par un réglage (`ExternalCpuLoad`, `ExternalGpuLoad`,
`GpuHardwareLimit`, `None`, `Unknown`) — la protection du conseiller est
garantie dans la CLI et dans tous les replis (`BottleneckTuning.IsTunable`).
Sous Windows, `tune <jeu>` reste un mode guidé en une commande (PresentMon
piloté) ; `tune <jeu> --baseline/--after` est une **simulation** qui n'écrit
rien ; `tune pending` liste les expériences en attente.

Chaque session `monitor` enregistre en plus son **plan exact** — y compris,
pour chaque réglage lié, le fichier, la section, la clé, la **valeur observée
et la valeur proposée**. `apply <jeu> --session <id>` refuse une session d'un
autre jeu, applique précisément ces valeurs, et laisse intacte toute clé dont
la valeur a changé depuis la session (le plan ne décrit plus la réalité).
Pour un plan moderne (avec instantanés), la correspondance **par nom n'existe
plus** : un conseil non lié à un fichier n'est jamais deviné ; le nom affiché
est de l'interface, pas un identifiant. Seules les sessions antérieures aux
instantanés conservent l'ancien repli par nom.

### Conseiller contextuel

Le moteur ne se contente pas de règles génériques : il croise **l'objectif
FPS**, **le matériel détecté**, **la télémétrie de la session** et **les
spécificités du jeu** pour produire un plan dimensionné et honnête.

- **Analyse d'écart** : avec `--target-fps` et une capture PresentMon
  (`--frametimes capture.csv`), le plan est dimensionné à l'écart réel —
  besoin de +5 % → une seule modification peu coûteuse ; besoin de +70 % →
  plan complet avec gains estimés en % et total attendu. Si l'objectif reste
  hors de portée, l'assistant le dit au lieu de promettre l'impossible.
- **Conscience du GPU** : DLSS n'est proposé qu'aux RTX, XeSS est préféré sur
  Intel Arc, FSR sert de repli universel ; la Frame Generation n'apparaît que
  sur RTX 40+ (ou FSR3), avec son avertissement de latence — et elle est
  proposée aux jeux **limités par le CPU**, l'un des rares leviers qui aide
  vraiment dans ce cas.
- **Conscience de la résolution** : à 1080p, le mode upscaling est plafonné à
  Qualité (en dessous, le rendu part de moins de 720p et devient flou) ; à
  4K, les modes agressifs deviennent pertinents.
- **Conscience de la VRAM** : goulot VRAM → le conseiller choisit le palier
  de textures le plus haut qui tient dans la carte (budgets par palier dans
  les profils), en citant les chiffres mesurés.
- **Spécificités du jeu** : cap moteur (Elden Ring : objectif ramené à 60
  avec explication), liste des upscalers réellement présents (aucun conseil
  DLSS pour un jeu qui n'en a pas), gains estimés par réglage issus des
  benchmarks publics et affinables par la communauté.
- **Cas « fluidité »** : moyenne au-dessus de l'objectif mais 1% low
  effondré → conseils anti-stutter (cap de framerate, pression VRAM/RAM),
  pas de baisse aveugle de qualité. Si tout va bien, l'assistant dit
  simplement qu'il n'y a rien à changer.

### Historique

Chaque session `monitor` est enregistrée dans une base SQLite locale
(`%LocalAppData%/Perfoptimize/perfoptimize.db`) ; `perfoptimize history [jeu]`
liste les dernières sessions. À terme, cet historique permettra de comparer
une machine à ses propres sessions passées et de partager des résultats entre
configurations similaires.

### Fichiers de configuration

Les profils de jeux documentent l'emplacement des fichiers de réglages
(`%DOCUMENTS%\The Witcher 3\user.settings`, `%LOCALAPPDATA%\...\UserSettings.json`…)
et leur format (INI, JSON, XML). Règles de sécurité, dans cet ordre :

1. **jamais de modification sans sauvegarde horodatée préalable** ;
2. **jamais d'écriture pendant que le jeu tourne** (il réécrirait le fichier
   en quittant) — la commande `--restore` refuse si le processus est détecté ;
3. l'éditeur INI ne touche que la ligne visée et préserve commentaires,
   ordre et espacement, car certains lanceurs valident leurs propres fichiers.

### Application des réglages

`apply <jeu>` traduit les recommandations en modifications concrètes des
fichiers de configuration. Les réglages liés déclarent dans le profil leur clé
et leur **échelle de valeurs** ordonnée de la plus basse à la plus haute
qualité (`"levels": ["0","1","2","3","4"]`) — « baisser d'un cran » = un pas
vers la gauche, ce qui gère aussi les échelles inversées comme
`TextureDownscale` où 0 est la meilleure qualité.

Sécurité avant tout :

- **dry run par défaut** : sans `--write`, rien n'est modifié, le plan est
  seulement affiché ;
- toute valeur actuelle hors de l'échelle documentée est **laissée intacte**
  (une mauvaise écriture est pire qu'une optimisation manquée) ;
- `--write` exige le jeu fermé, sauvegarde chaque fichier avant modification,
  et `config <jeu> --restore` ramène l'état précédent ;
- le goulot ciblé vient de la dernière session `monitor` enregistrée pour ce
  jeu, ou de `--bottleneck` explicite.

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
├── Perfoptimize.Core/          # bibliothèque partagée (moteur commun CLI + UI)
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
│   ├── Monitoring/                 # étapes 3 et 5 : télémétrie et frametimes
│   │   ├── TelemetrySample.cs
│   │   ├── PerformanceMonitor.cs
│   │   ├── PerformanceSummary.cs
│   │   ├── Frametimes/             # statistiques FPS + parseur CSV PresentMon
│   │   ├── Windows/                # compteurs WMI (GPU Engine, GPU Adapter Memory)
│   │   └── Linux/                  # /proc/stat, hwmon, nvidia-smi
│   ├── Optimization/               # analyse, objectifs, recommandations, glossaire
│   │   ├── BottleneckDetector.cs
│   │   ├── RecommendationEngine.cs
│   │   ├── SystemAdvisor.cs        # conseils PC globaux
│   │   ├── OptimizationLoop.cs     # boucle mesurée avant/après (tune)
│   │   └── SettingsGlossary.cs     # explication des réglages + choix d'API (DX11/12/Vulkan)
│   ├── Live/                       # analyse continue temps réel
│   │   ├── LiveMonitor.cs          # échantillonneur (process + télémétrie + FPS)
│   │   ├── LiveUsageCalculator.cs  # calcul pur : CPU%, top process, alertes
│   │   ├── AlertTracker.cs         # alertes à identité stable (pur)
│   │   ├── FpsChangeDetector.cs    # détection auto des changements de FPS (pur)
│   │   ├── DpcLatencyReader.cs     # % DPC/ISR (Windows)
│   │   └── SystemMemoryReader.cs
│   ├── Diagnostics/                # incidents de stabilité
│   │   ├── IncidentClassifier.cs   # événement → incident expliqué (pur)
│   │   └── WindowsEventLogReader.cs
│   ├── Network/                    # inventaire réseau + attribution
│   │   ├── NetworkAnalyzer.cs · NetstatParser.cs · ConnectionClassifier.cs   # purs, testés
│   │   ├── ProcessConnectionReader.cs · HostResolver.cs
│   │   └── NetworkReader.cs
│   ├── Library/                    # découverte des jeux installés + lecture des configs
│   ├── Configs/                    # fichiers de config et application (backup, INI chirurgical)
│   ├── Storage/                    # historique SQLite des sessions
│   ├── Tools/                      # installation de PresentMon
│   └── Formatting/
├── Perfoptimize.App/           # application de bureau (serveur local + cockpit)
│   ├── Program.cs                  # endpoints HTTP (minimal API)
│   ├── LiveState.cs                # état partagé du moniteur (singleton)
│   ├── LiveMonitorHostedService.cs # échantillonnage continu en arrière-plan
│   └── WebAssets/                  # cockpit vanilla : index.html, styles.css, app.js
└── Perfoptimize.Cli/           # console (scan, games, monitor, frametimes, history, config, tune)
tests/
└── Perfoptimize.Core.Tests/    # tests unitaires (parseurs, règles, heuristiques, détecteurs)
```

### Développement

```bash
dotnet build Perfoptimize.sln
dotnet test Perfoptimize.sln
```

### Limites connues

Un outil de diagnostic doit être honnête sur ce qu'il ne sait pas encore faire :

- **La partie Windows n'a pas été validée sur une machine Windows réelle** —
  elle compile et sa logique pure est testée (parseurs de compteurs, règles),
  mais les requêtes WMI/compteurs restent à éprouver sur de vraies configs ;
- **Seuls les fichiers INI sont modifiables** : Cyberpunk 2077 (JSON) et
  GTA V (XML) sont localisés et sauvegardables, mais pas encore écrits ;
- **La détection multi-écrans est heuristique** (résolutions par adaptateur
  WMI, premier écran supposé principal) — les API `QueryDisplayConfig`
  restent à intégrer ;
- **La température CPU Windows** (`MSAcpi_ThermalZoneTemperature`) peut
  refléter le chipset plutôt que le CPU — elle n'est utilisée que comme
  suspicion, jamais comme preuve ;
- **Les fichiers de config sans BOM ni octets invalides** sont supposés UTF-8 ;
  seul un contenu réellement invalide en UTF-8 déclenche le repli Latin-1 ;
- **Les gains estimés des profils sont déclaratifs** (ordres de grandeur des
  benchmarks publics), en attendant la boucle mesure → application →
  re-mesure qui les calibrera sur la machine de l'utilisateur ;
- **Le runtime « cockpit » est surtout Windows** : FPS live (PresentMon),
  températures/DPC (WMI), incidents (journal d'événements), attribution réseau
  (`netstat -ano`) fonctionnent pleinement sous Windows ; Linux est partiel et
  ces chemins **compilent mais restent à éprouver sur une vraie machine** ;
- **La surveillance continue ne vit que tant que l'application est ouverte** :
  elle est indépendante de la section affichée, mais ne survit pas à la
  fermeture du processus (un mode service Windows / tâche planifiée reste à
  faire) ;
- **Les corrections système actionnables démarrent, au niveau « prudent »** :
  la première est *désactiver un programme au démarrage* (clé Run du registre,
  dossier Démarrage ou tâche planifiée) — proposée, prévisualisée, **sauvegardée
  puis appliquée seulement après confirmation**, et réversible d'un clic
  (« Réactiver »). Rien n'est jamais modifié en silence. Les autres corrections
  (nettoyage de caches, plan d'alimentation, arrêt d'un processus parasite)
  restent à faire sur le même modèle ;
- **Interface en français uniquement**, thème sombre unique, couleur
  porteuse d'information (rouge/vert) — internationalisation et accessibilité
  restent à faire ;
- **Pas encore de tests frontend / end-to-end** ni de fichier de licence.

### Installation & mises à jour

L'application se distribue en **installeur Windows avec auto-update**, via
[Velopack](https://velopack.io) et les GitHub Releases :

- **Publier une version** : pousser un tag semver (`git tag v0.18.0 && git push
  origin v0.18.0`). Le workflow `.github/workflows/release.yml` publie un build
  autonome `win-x64`, le empaquette avec Velopack (installeur `Setup.exe` +
  paquets de mise à jour delta) et téléverse le tout sur la Release du tag.
- **Côté application** : au démarrage, `/api/update` interroge les Releases ;
  si une version plus récente existe, un bandeau « Mise à jour disponible »
  apparaît. **Rien n'est téléchargé sans action de l'utilisateur** — le bouton
  « Mettre à jour » télécharge puis redémarre sur la nouvelle version (même
  principe prudent que le reste de l'app). En développement (build non
  installé), la vérification se désactive proprement.

### Prochaines étapes

1. Validation sur plusieurs vraies machines Windows (compteurs GPU réels,
   PresentMon, WMI, `netstat`, journal d'événements, multi-GPU) ;
2. Tests d'intégration des endpoints + tests frontend (harness navigateur en CI) ;
3. Étendre les corrections actionnables (désactivation au démarrage déjà en
   place) : nettoyage caches, TRIM, plan d'alimentation, arrêt de processus
   parasite — toujours avec prévisualisation + sauvegarde + confirmation ;
4. Surveillance qui survit à la fermeture (service Windows / tâche planifiée) ;
5. Écriture JSON/XML (Cyberpunk 2077, GTA V), plus de profils de jeux, et
   mapping glossaire ↔ réglages du jeu affiché en direct ;
6. Licence et binaire signé (l'installeur Windows + l'auto-update via Velopack
   et GitHub Releases sont en place, voir « Installation & mises à jour ») ;
7. Internationalisation (FR/EN) et passe d'accessibilité (clavier, contraste,
   `prefers-reduced-motion`).
