# GRW Efficiency Mode

**English** · [Français](#français)

A small Windows tool that launches **Tom Clancy's Ghost Recon Wildlands** and applies Windows **Efficiency Mode** (EcoQoS) to the game while it starts up, then gives control back to Windows.

## Features

- **Detects the game automatically**: Steam version (checked first, in every Steam library) or Ubisoft Connect version.
- **Launches the game** through the right platform.
- **Enables Efficiency Mode** on every new `GRW.exe` instance during startup.
- **Restores normal behavior** 120 seconds after the last new instance.
- **Exits on its own** 15 seconds after the game is closed, or after 5 minutes if the game never starts.
- **Never changes process priority.**
- **No window**: shows up as **GRW Efficiency Mode** in Task Manager.
- **No administrator rights** needed, to install or to run.

## Requirements

- Windows 10 (21H2 or later) or Windows 11
- .NET Framework 4.x (built into Windows)
- Ghost Recon Wildlands installed via Steam or Ubisoft Connect

## Installation

1. Download `GRW-Efficiency-Mode-Setup.exe` from the **Releases** page.
2. Run it. If Windows blocks it, see *Windows warnings* below.
3. Choose the install folder (default: `%LOCALAPPDATA%\Programs\GRW Efficiency Mode`).
4. Choose the options, then click **Install**:
   - add a shortcut to the Desktop;
   - delete the setup file when done.

The shortcut uses the original game icon. To pin it: right-click the shortcut → **Pin to taskbar** or **Pin to Start**.

## Usage

Launch the game with the **GRW Efficiency Mode** shortcut instead of Steam or Ubisoft Connect.

Log file: `%LOCALAPPDATA%\GRW-Efficiency-Mode\GRW-Efficiency-Mode.log`

## Windows warnings

The executable is not code-signed, so Windows may warn on the first run:

- **SmartScreen** ("Windows protected your PC"): click **More info** → **Run anyway**.
- **Smart App Control** blocks the file with no "Run anyway" option. Right-click the file → **Properties** → tick **Unblock** → **OK**, then run it again. This may not be enough on every PC.

Once installed, the app no longer carries the "downloaded from the Internet" mark.

## How it works

The file name decides the mode. If it contains `Setup`, the program installs itself: it copies itself as `GRW Efficiency Mode.exe` and creates the shortcuts. Any other name launches the game and applies Efficiency Mode.

The app only uses public Windows APIs. It runs `SetProcessInformation` with `ProcessPowerThrottling` to turn EcoQoS on, then resets it so Windows decides again.

## Settings

The timings are constants at the top of the `Launcher` class in `src/GRWEfficiencyMode.cs`:

| Constant | Default | Meaning |
|---|---|---|
| `RestoreDelaySeconds` | 120 | Delay before Efficiency Mode is removed |
| `MaxWatchSeconds` | 600 | Hard limit after the first detection |
| `MaxWaitForGameSeconds` | 300 | Give up if the game never starts |
| `GraceAfterCloseSeconds` | 15 | Game gone for this long = closed |

## Building

Run `build.cmd` on Windows. It uses the C# compiler built into Windows and writes `dist\GRW-Efficiency-Mode-Setup.exe`.

The GitHub Actions workflow builds the same file on every `v*` tag and attaches it to a release.

## Uninstall

Use **Uninstall GRW Efficiency Mode** in the Start menu, or run the setup again and click **Uninstall**. This removes the app, its shortcuts and its log files.

## Repository layout

| Path | Content |
|---|---|
| `src/GRWEfficiencyMode.cs` | Full source code (setup + launcher) |
| `src/app.ico` | Application icon |
| `src/app.manifest` | Windows manifest (no administrator rights) |
| `build.cmd` | Local build script |
| `.github/workflows/build.yml` | Automatic build and release |

## License

MIT — see `LICENSE`. Ghost Recon Wildlands is a trademark of Ubisoft; this project is not affiliated with Ubisoft or Valve.

---

# Français

**Français** · [English](#grw-efficiency-mode)

Un petit outil Windows qui lance **Tom Clancy's Ghost Recon Wildlands** et applique le **mode efficacité** de Windows (EcoQoS) au jeu pendant son démarrage, puis rend la main à Windows.

## Fonctionnalités

- **Détection automatique du jeu** : version Steam (vérifiée en premier, dans toutes les bibliothèques Steam) ou version Ubisoft Connect.
- **Lancement du jeu** par la bonne plateforme.
- **Activation du mode efficacité** sur chaque nouvelle instance de `GRW.exe` pendant le démarrage.
- **Retour au comportement normal** 120 secondes après la dernière nouvelle instance.
- **Fermeture automatique** 15 secondes après la fermeture du jeu, ou au bout de 5 minutes si le jeu ne démarre jamais.
- **La priorité du processus n'est jamais modifiée.**
- **Aucune fenêtre** : apparaît sous le nom **GRW Efficiency Mode** dans le Gestionnaire des tâches.
- **Aucun droit administrateur** nécessaire, ni pour installer ni pour lancer.

## Prérequis

- Windows 10 (21H2 ou plus récent) ou Windows 11
- .NET Framework 4.x (intégré à Windows)
- Ghost Recon Wildlands installé via Steam ou Ubisoft Connect

## Installation

1. Télécharge `GRW-Efficiency-Mode-Setup.exe` depuis la page **Releases**.
2. Lance-le. Si Windows le bloque, voir *Avertissements Windows* plus bas.
3. Choisis le dossier d'installation (par défaut : `%LOCALAPPDATA%\Programs\GRW Efficiency Mode`).
4. Choisis les options, puis clique sur **Install** :
   - ajouter un raccourci sur le Bureau ;
   - supprimer le fichier d'installation à la fin.

Le raccourci utilise l'icône originale du jeu. Pour l'épingler : clic droit sur le raccourci → **Épingler à la barre des tâches** ou **Épingler à l'écran de démarrage**.

## Utilisation

Lance le jeu avec le raccourci **GRW Efficiency Mode** au lieu de Steam ou d'Ubisoft Connect.

Fichier de log : `%LOCALAPPDATA%\GRW-Efficiency-Mode\GRW-Efficiency-Mode.log`

## Avertissements Windows

L'exécutable n'est pas signé numériquement, Windows peut donc afficher un avertissement au premier lancement :

- **SmartScreen** (« Windows a protégé votre ordinateur ») : clique sur **Informations complémentaires** → **Exécuter quand même**.
- **Contrôle intelligent des applications** bloque le fichier sans option « Exécuter quand même ». Fais clic droit sur le fichier → **Propriétés** → coche **Débloquer** → **OK**, puis relance-le. Ça peut ne pas suffire sur tous les PC.

Une fois installée, l'application ne porte plus la marque « téléchargé depuis Internet ».

## Fonctionnement

Le nom du fichier décide du mode. S'il contient `Setup`, le programme s'installe : il se copie sous le nom `GRW Efficiency Mode.exe` et crée les raccourcis. Avec tout autre nom, il lance le jeu et applique le mode efficacité.

L'application utilise uniquement des API Windows publiques. Elle appelle `SetProcessInformation` avec `ProcessPowerThrottling` pour activer EcoQoS, puis le réinitialise pour que Windows décide à nouveau.

## Réglages

Les délais sont des constantes en haut de la classe `Launcher` dans `src/GRWEfficiencyMode.cs` :

| Constante | Défaut | Rôle |
|---|---|---|
| `RestoreDelaySeconds` | 120 | Délai avant le retrait du mode efficacité |
| `MaxWatchSeconds` | 600 | Limite maximale après la première détection |
| `MaxWaitForGameSeconds` | 300 | Abandon si le jeu ne démarre jamais |
| `GraceAfterCloseSeconds` | 15 | Jeu absent pendant ce délai = fermé |

## Compilation

Lance `build.cmd` sous Windows. Il utilise le compilateur C# intégré à Windows et produit `dist\GRW-Efficiency-Mode-Setup.exe`.

Le workflow GitHub Actions compile ce même fichier à chaque tag `v*` et le joint à une release.

## Désinstallation

Utilise **Uninstall GRW Efficiency Mode** dans le menu Démarrer, ou relance l'installeur et clique sur **Uninstall**. Cela supprime l'application, ses raccourcis et ses fichiers de log.

## Structure du dépôt

| Chemin | Contenu |
|---|---|
| `src/GRWEfficiencyMode.cs` | Code source complet (installation + lanceur) |
| `src/app.ico` | Icône de l'application |
| `src/app.manifest` | Manifeste Windows (sans droits administrateur) |
| `build.cmd` | Script de compilation locale |
| `.github/workflows/build.yml` | Compilation et release automatiques |

## Licence

MIT — voir `LICENSE`. Ghost Recon Wildlands est une marque d'Ubisoft ; ce projet n'est affilié ni à Ubisoft ni à Valve.
