# Talk2Tia — Per iniziare

> **Beta pubblica (v0.9).** Talk2Tia è completo ma ancora in beta: usalo su progetti di prova, verifica ogni modifica
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

## Variabili d'ambiente

| Variabile | Default | Scopo |
|---|---|---|
| `TALK2TIA_TOOLSETS` | `all` | Gruppi di tool da esporre, separati da virgola: `portal,hardware,plc,online,library,hmi,object`. Meno tool = meno contesto per il modello. |
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
