> **Beta pubblica (v0.10).** Talk2Tia è completo ma ancora in beta: usalo su progetti di prova, verifica ogni modifica
> prima di salvare o scaricare, e segnala i problemi su [GitHub](https://github.com/insideapp-srl/talk2tia/issues).

Talk2Tia è un server MCP che permette a un assistente AI (Claude Code, Claude Desktop o qualunque client MCP)
di lavorare dentro Siemens TIA Portal V21 tramite l'API ufficiale Openness.

## Requisiti

- Windows 10/11 x64
- TIA Portal V21 con l'opzione **TIA Portal Openness** installata
- L'utente Windows deve far parte del gruppo locale **Siemens TIA Openness**
  (dopo l'aggiunta serve un logout e un nuovo login)
- .NET Framework 4.8 (incluso in Windows 10/11)

Lancia `talk2tia --doctor` per verificare l'ambiente.

## Compilare dai sorgenti

```powershell
dotnet build -c Release
dotnet test
```

Gli assembly Siemens vengono referenziati dall'installazione di TIA Portal
(`C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`) e non vengono mai copiati né inclusi nel repository.
Puoi cambiare il percorso con `dotnet build /p:TiaPublicApiDir=...` oppure, a runtime, con `TALK2TIA_PUBLICAPI_DIR`.

L'eseguibile si trova in `src\Talk2Tia.Server\bin\<Configuration>\net48\talk2tia.exe`.

## Configurare un client MCP

### Claude Code

```powershell
claude mcp add talk2tia -- "C:\Program Files\Talk2Tia\talk2tia.exe"
```

### Claude Desktop (`claude_desktop_config.json`)

```json
{
  "mcpServers": {
    "talk2tia": {
      "command": "C:\\Program Files\\Talk2Tia\\talk2tia.exe",
      "env": { "TALK2TIA_TOOLSETS": "all" }
    }
  }
}
```

## Configurazione

Talk2Tia funziona senza configurazione. Un file JSON facoltativo decide quali tool riceve l'assistente e quali PLC può
leggere in runtime. Talk2Tia lo cerca in quest'ordine:

1. `--config <percorso>` sulla riga di comando;
2. la variabile d'ambiente `TALK2TIA_CONFIG`;
3. `%APPDATA%\Talk2Tia\config.json`, se esiste.

```jsonc
{
  // "full" (default) oppure "readonly"
  "profile": "readonly",
  // Facoltativo: gruppi di tool da esporre (default: tutti). "portal" è sempre incluso.
  "toolsets": ["plc", "online", "diagnostics"],
  // Facoltativo: tool aggiunti a profilo e gruppi. * è un carattere jolly.
  "allow": ["plc_compile"],
  // Facoltativo: tool tolti in ogni caso. Deny vince sempre.
  "deny": ["obj_*"],
  // Facoltativo: PLC raggiungibili tramite la Web API della CPU, per i tool di diagnostica.
  "plcs": {
    "Linea1": {
      "address": "192.168.0.10",
      "username": "talk2tia",
      "passwordEnv": "TALK2TIA_LINEA1_PASSWORD",
      "certificateThumbprint": "3F2A...C9"
    }
  }
}
```

I tool vengono scelti in quest'ordine: `toolsets`, poi `profile`, poi `allow`, infine `deny`. I tool esclusi non vengono
proprio registrati, quindi l'assistente non li vede. Un nome sconosciuto in `allow` o `deny` blocca l'avvio con un errore,
così gli errori di battitura emergono subito. Con `talk2tia --doctor --config <percorso>` controlli il file: mostra il
profilo, quanti tool sono esposti e se ogni PLC risponde.

### Profilo sola lettura

`"profile": "readonly"` (oppure `--read-only`, oppure `TALK2TIA_READONLY=1`) espone solo i tool che leggono: blocchi, tag,
tipi, hardware, librerie, riferimenti incrociati, confronto online e diagnostica. Mantiene quello che serve per lavorare
senza modificare nulla: collegarsi a TIA Portal o avviarlo, aprire, selezionare e chiudere progetti, aprire librerie globali, configurare la
connessione online, andare online e offline, fare snapshot delle DB. Restano esclusi scrittura di codice, tag e hardware,
salvataggio, compilazione, modifiche alle librerie, download, `obj_set`/`obj_invoke`/`obj_create` e le azioni in runtime.
Puoi riaggiungere singoli tool con `allow`, per esempio `"allow": ["plc_compile"]`.

Per dare permessi diversi a client diversi, indica a ciascuno il proprio file con `TALK2TIA_CONFIG` nella sua
configurazione MCP.

### Web API del PLC (diagnostica)

I tool `diagnostics` leggono la CPU in funzione tramite la sua Web API: modo operativo, buffer di diagnostica, allarmi,
valori delle variabili e tabelle di controllo. CPU supportate: S7-1500 e CPU ET 200SP, S7-1200 G2 e PLCSIM Advanced con
web server. Buffer di diagnostica e allarmi richiedono firmware S7-1500 V3.0 o successivo; `diag_webapi_methods` mostra
cosa supporta una CPU.

Sulla CPU, in TIA Portal (*Proprietà > Web server*), poi carica la configurazione hardware:

1. attiva il web server sull'interfaccia usata dal PC;
2. in *Gestione utenti* aggiungi un utente per Talk2Tia con i diritti necessari: *lettura valori* e *lettura diagnostica*
   per il gruppo `diagnostics`; *scrittura valori*, *cambio modo operativo* e *tacitazione allarmi* solo se abiliti anche
   `diagnostics_write`;
3. rendi accessibili le variabili: *Accessibile da HMI/OPC UA/Web API* nelle DB e nelle tabelle delle variabili.

Nel file di configurazione, `passwordEnv` è il nome della variabile d'ambiente che contiene la password: la password non va
mai nel file. Impostala nella configurazione del client MCP (`env`) o nelle variabili d'ambiente dell'utente Windows.

Di default la CPU usa un certificato autofirmato. Rendilo affidabile con `certificateThumbprint` (SHA-1 o SHA-256, dal
certificato mostrato dal browser), oppure installa il certificato della CPU nell'archivio certificati di Windows.
`"allowUntrustedCertificate": true` salta il controllo ed è pensato solo per reti di test isolate. `timeoutSeconds` imposta
il timeout delle richieste (default 10).

Il gruppo `diagnostics_write` (`diag_write_variables`, `diag_set_operating_mode`, `diag_acknowledge_alarm`,
`diag_webapi_call`) agisce sull'impianto in funzione. Non fa mai parte del profilo sola lettura e ogni chiamata richiede
`confirm=true`. Per escluderlo del tutto, elenca in `toolsets` i gruppi che vuoi senza `diagnostics_write`.

`diag_db_snapshot` non usa la Web API: legge i valori attuali di una DB tramite TIA Portal mentre il PLC è raggiungibile
online, come *Snapshot dei valori attuali* nell'editor della DB.

## Variabili d'ambiente

| Variabile | Default | Scopo |
|---|---|---|
| `TALK2TIA_CONFIG` | — | Percorso del file di configurazione. |
| `TALK2TIA_TOOLSETS` | `all` | Gruppi di tool da esporre, separati da virgola: `portal,hardware,plc,online,diagnostics,diagnostics_write,library,hmi,object`. Sostituisce `toolsets` del file. Meno tool = meno contesto per il modello. |
| `TALK2TIA_READONLY` | `0` | `1` forza il profilo sola lettura. |
| `TALK2TIA_PUBLICAPI_DIR` | dal registro | Cartella con `Siemens.Engineering.*.dll` (V21, net48). |

## Prima sessione

1. Apri TIA Portal e il tuo progetto (oppure lascia che l'assistente ne avvii uno con `portal_start`).
2. Chiedi all'assistente di collegarsi: chiamerà `portal_attach`. TIA Portal mostra il popup di accesso Openness: confermalo.
3. Lavora in linguaggio naturale, per esempio:
   - "Elenca i blocchi di PLC_1 non consistenti e compila il software."
   - "Leggi FB_Conveyor in SCL, aggiungi un timer di rilevamento inceppamento e riscrivilo."
   - "Modifica questa rete di OB1 (KOP) per usare il nuovo FB motore."
   - "Crea un tipo di libreria da FB_Motor, rilascia la versione 1.0.0 e aggiorna tutti i PLC."
4. Le modifiche sono subito visibili nel progetto aperto ma **non vengono salvate** finché non chiami `project_save` (o salvi da TIA Portal).

## Come funziona la modifica del codice

| Linguaggio | Lettura | Scrittura |
|---|---|---|
| SCL / STL / DB / UDT | `plc_read_block` / `plc_read_type` con formato `scl` | `plc_write_scl` (sorgente testuale, tramite una sorgente esterna temporanea) |
| KOP / FUP (LAD / FBD) | `plc_read_block` con formato `document` (SIMATIC SD `.s7dcl` + `.s7res`) | `plc_write_document` |
| Qualunque linguaggio | formato `xml` (SimaticML) | `plc_write_xml` |

Dopo ogni scrittura lancia sempre `plc_compile`: il tool restituisce errori e warning con la loro posizione.

## Controllare le modifiche

Ogni tool di scrittura (`plc_write_scl`, `plc_write_document`, `plc_write_xml`, `plc_write_type`) esporta il blocco prima e
dopo la modifica e restituisce un `diff` in Markdown, così l'assistente può mostrare esattamente cosa è cambiato in TIA
Portal:

- **SCL, DB, UDT**: diff unificato del sorgente (righe `+` aggiunte, `-` tolte, tre righe di contesto);
- **KOP e FUP**: solo le reti cambiate, disegnate prima e dopo, con l'elenco degli elementi aggiunti e tolti, segnati anche
  nel disegno. Le reti SCL dentro un blocco KOP e le modifiche all'interfaccia sono mostrate come diff.

```text
┃                #MachineValve.Command    #MachineValve.Command
┃ + #DemoEnable        .LifeBit               .CommandRest
┠──────┤ ├────────────────┤/├───────────┬──────────(R)──────────
┃                                       │ #MachineValve.Command
┃                                       │     .CommandWork
┃                                       └──────────(R)──────────
```

`plc_diff_block` mostra la diff di una modifica proposta senza scriverla, per controllarla prima di applicarla.
`plc_render_block` disegna su richiesta le reti di qualunque blocco KOP/FUP. Con `showDiff=false` un tool di scrittura non
calcola la diff.

### La skill talk2tia-diff

Il repository include una [Agent Skill](https://github.com/insideapp-srl/talk2tia/tree/main/skills/talk2tia-diff) che
insegna all'assistente a mostrare sempre queste diff, a lasciare intatti i disegni, a spiegarli e a proporre un controllo
prima delle modifiche grandi. È allegata anche a ogni release come `talk2tia-diff-skill.zip`.

- **Claude Code**: copia la cartella `talk2tia-diff` in `%USERPROFILE%\.claude\skills\` (tutti i progetti) oppure in
  `.claude\skills\` dentro un progetto.
- **Claude Desktop / claude.ai**: carica `talk2tia-diff-skill.zip` in *Impostazioni > Funzionalità > Skill*.

I disegni sono testo semplice, quindi si vedono uguali in tutti i client, terminale compreso. Il report HTML è facoltativo
e viene creato solo su richiesta.

## Sicurezza

- I tool distruttivi richiedono `confirm=true` (o `overwrite=true` per sostituire codice). L'assistente è istruito a chiedere prima.
- `plc_download` agisce sull'impianto reale: richiede un'approvazione esplicita, non ferma mai la CPU se non con `allowStop=true`
  e non reinizializza mai i blocchi dati se non con `reinitializeDataBlocks=true`.
- Ogni chiamata Openness gira su un unico thread STA dedicato, come richiesto dall'API.

## Limiti noti

- Le finestre di download che richiedono una password (CPU protette) vengono segnalate ma non compilate.
- Le schermate WinCC Unified non hanno ancora un tool di export dedicato: usa i tool `obj_*`.
- Le protezioni aziendali degli endpoint (ad esempio le regole ASR "prevalence" di Defender) possono bloccare un
  `talk2tia.exe` appena compilato e non firmato. Le release vanno firmate; in sviluppo chiedi all'IT un'esclusione per la cartella.

## Esclusione di responsabilità

Talk2Tia è fornito "così com'è", senza garanzie di alcun tipo. InsideApp S.r.l. non risponde di danni, perdite di dati,
fermi produttivi, lesioni o altre conseguenze derivanti dal suo utilizzo, comprese le modifiche apportate da assistenti
AI a progetti, programmi PLC o impianti. Ogni modifica deve essere verificata, collaudata e approvata da personale
qualificato prima di arrivare su una macchina reale. I termini completi sono nella licenza e nelle note legali del sito.
