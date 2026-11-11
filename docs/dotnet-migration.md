# Migration de LiteDeploy vers .NET 10 : évaluation

*Novembre 2026. Base : branche du lot 1 et du lot 2 (fuites corrigées, package signé).*

## État actuel

| Élément | Valeur |
|---|---|
| Langage | C# (`LangVersion` 7.3 en x64, défaut du compilateur en x86) |
| Projets | Un seul : `LiteDeploy.csproj`, exe WinForms, environ 9 600 lignes |
| Format | Ancien format MSBuild (non SDK), avec les restes d'une publication ClickOnce |
| Framework | .NET Framework 4.8 (`v4.8`) |
| Configurations | `Debug` et `Release`, en `x86` (en réalité AnyCPU ou x64 selon la configuration) et en `x64` |
| Dépendances | Uniquement des assemblys du framework, plus une référence COM `WindowsInstaller` (msi.dll, interop généré par tlbimp et embarqué) |
| DLL précompilées | Aucune |
| Outils tiers à l'exécution | `PsExec` (Sysinternals, non redistribué) ; `RDPWInst.exe` et `RemoteManagement.msc` (optionnels) |

## Bloquants pour `net10.0-windows`

| Bloquant | Présent ? | Détail | Effort |
|---|---|---|---|
| `ContextMenu`, `MainMenu`, `MenuItem`, `StatusBar` (retirés de WinForms) | **Non** | L'interface utilise déjà `MenuStrip`, `ContextMenuStrip` et `StatusStrip` | 0 |
| .NET Remoting | Non | | 0 |
| `BinaryFormatter` dans le code | Non | Les fichiers de cache (`adcache.xml`) et les sorties passent par `XmlSerializer` | 0 |
| `BinaryFormatter` dans les ressources | **Oui** | 18 entrées `.resx` sérialisées en binaire, dont 3 `ImageListStreamer` (`PingAll`, `PCListDesigner`, `ProcessConsole`). WinForms .NET 9+ les lit sans `BinaryFormatter` (décodeur NRBF), mais il faut le vérifier pour chaque formulaire, ou régénérer les ressources | 0,5 à 1 j |
| `Thread.Abort` (lève `PlatformNotSupportedException` sur .NET 5+) | **Oui, 3 appels** | `ProcessManager.KillAll`, `ActiveDir.StopUpdate`, `DNSScanner` (arrêt des recherches DNS). À remplacer par une annulation coopérative (`CancellationToken`, ou fin du processus PsExec) | 0,5 à 1 j |
| Référence COM `WindowsInstaller` (tlbimp) | **Oui** | Ne compile pas avec `dotnet build` (MSBuild complet obligatoire), ou à remplacer par quelques P/Invoke `msi.dll` (`MsiOpenDatabase`, `MsiDatabaseOpenView`...) | 0,5 j |
| `System.DirectoryServices` | Oui | Disponible en paquet NuGet (Windows seulement) | 0,1 j |
| `System.Management` (WMI) | Non | | 0 |
| Services Windows | Non | | 0 |
| `Process.Start(fichier)` | Oui, 1 cas | Ouverture du journal MSI : `UseShellExecute` vaut `false` par défaut sur .NET Core, il faut le passer à `true` | 0,1 j |
| Références inutilisées | Oui | `System.Deployment` (n'existe plus), `System.Windows.Forms.DataVisualization` (non fournie) : jamais utilisées, à retirer | 0 |
| DLL précompilées | Non | | 0 |

## Coût hors code

- **Runtime** : .NET Framework 4.8 est inclus dans Windows 10 et 11 et suit leur cycle de vie. Une application `net10.0-windows` exige soit le **.NET Desktop Runtime 10** sur chaque poste d'administration, soit une publication autonome d'environ 60 à 150 Mo au lieu d'environ 1 Mo aujourd'hui.
- **Support** : .NET 10 LTS est supporté jusqu'en novembre 2028. Il faudra donc migrer de nouveau vers .NET 12, ou une version ultérieure, dans environ deux ans. .NET Framework 4.8 n'a pas de date de fin annoncée hors du cycle de vie de Windows.
- **Suite** : LiteTask et LitePM sont aussi en .NET Framework 4.8. Migrer LiteDeploy seul ajoute un runtime de plus aux postes, sans bénéfice pour la suite.
- **Tests** : l'application n'a pas de tests automatisés. Il faut une recette manuelle complète (déploiement, MSI, AD, ping, scan DNS, consoles) et un passage du banc de mesure du lot 1.

## Estimation

| Poste | Effort |
|---|---|
| Correctifs de code (`Thread.Abort`, MSI, `Process.Start`, références) | 1,5 à 2,5 j |
| Ressources binaires (vérification ou régénération) | 0,5 à 1 j |
| Projet, CI, package (runtime ou publication autonome), contrat LiteTask | 0,5 à 1 j |
| Recette et mesure | 1 à 2 j |
| **Total** | **environ 4 à 6 jours**, plus le déploiement du runtime sur les postes |

## Recommandation

**Ne pas migrer vers .NET 10 maintenant.** Le bénéfice est faible pour un outil d'administration peu développé, utilisé sur quelques postes : performances WinForms comparables, aucune fonctionnalité attendue qui l'exige. Le coût est réel : runtime à déployer ou package 60 à 150 fois plus gros, nouvelle migration à la fin du support de .NET 10, divergence avec LiteTask et LitePM.

À la place, ce lot :

1. passe le projet au **format SDK en restant sur `net48`** : projet lisible, `msbuild -restore`, prêt pour une migration future qui se réduira à changer `TargetFramework` et à traiter les bloquants ci-dessus ;
2. prouve que la sortie de compilation est identique, avec un workflow temporaire de comparaison, retiré avant la fusion.

Si la migration redevient souhaitable, par exemple quand toute la suite passera à .NET, commencez par retirer les trois `Thread.Abort` et la référence COM. Ces changements valent aussi sous .NET Framework et réduisent la migration à un changement de projet.
