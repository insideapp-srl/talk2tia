import { stats } from './site';

export const languages = { en: 'English', it: 'Italiano' } as const;
export type Lang = keyof typeof languages;
export const defaultLang: Lang = 'en';

/** Site-relative path for a language: English lives at the root, Italian under /it. */
export function localePath(lang: Lang, path = '/'): string {
  const clean = path.startsWith('/') ? path : `/${path}`;
  if (lang === defaultLang) return clean;
  return clean === '/' ? '/it/' : `/it${clean}`;
}

/** Strips the language prefix: /it/docs -> /docs. */
export function pathWithoutLocale(pathname: string, base: string): string {
  let p = pathname.startsWith(base) ? pathname.slice(base.length - (base.endsWith('/') ? 1 : 0)) : pathname;
  if (!p.startsWith('/')) p = `/${p}`;
  p = p.replace(/^\/it(\/|$)/, '/');
  return p;
}

const en = {
  beta: {
    label: 'Public beta',
    text: 'Talk2Tia v0.9 is in public beta: use it on test projects and expect rough edges.',
    link: 'Send feedback',
  },
  meta: {
    title: 'Talk2Tia — Talk to TIA Portal',
    description:
      'MCP server for Siemens TIA Portal V21: your AI assistant reads and writes SCL and LAD blocks, manages libraries, compiles and downloads through Openness.',
  },
  nav: {
    features: 'Features',
    how: 'How it works',
    tools: 'Tools',
    docs: 'Docs',
    download: 'Download',
    menu: 'Open menu',
    theme: 'Toggle light/dark theme',
    language: 'Language',
    skip: 'Skip to content',
  },
  hero: {
    badge: 'MCP server for TIA Portal V21',
    titleA: 'Talk to',
    titleB: 'TIA Portal',
    titleC: 'It listens.',
    lead:
      'Talk2Tia connects your AI assistant to TIA Portal through the official Openness API. Ask in plain language and it writes SCL and LAD, manages libraries, compiles and checks — inside the project you have open.',
    ctaDownload: 'Download for Windows',
    ctaDocs: 'Get started in 5 minutes',
    note: `Free & open source (MIT) · ${stats.tools} tools · Windows x64 · TIA Portal V21`,
    demoLabel: 'Example conversation with Talk2Tia',
    demoUser: 'Add jam detection to FB_Conveyor: if the sensor stays covered for more than 5 s, stop the motor.',
    demoCompile: 'plc_compile · 0 errors, 0 warnings',
    demoBot: 'Done. FB_Conveyor compiles without errors. I added the TON timer <code>JamTimer</code> and the <code>JamAlarm</code> output. Shall I save the project?',
  },
  features: {
    badge: 'Features',
    title: 'All of TIA Portal, in plain language.',
    lead: 'From a single LAD network to your company library: Talk2Tia gives an AI assistant what you do every day in TIA Portal.',
    items: [
      { icon: 'code', tone: '', title: 'Writes SCL and LAD', text: 'Reads and edits SCL blocks as source, and LAD/FBD through the SIMATIC SD text documents. SimaticML covers everything else.' },
      { icon: 'layers', tone: 'teal', title: 'Libraries, for real', text: 'Types and versions, edit and release, master copies, project and library updates, harmonize and clean-up: the same commands as TIA Portal.' },
      { icon: 'check', tone: 'success', title: 'Compiles and checks', text: 'Compiles hardware, software or a single block and returns errors and warnings with their location, so the assistant fixes them itself.' },
      { icon: 'cpu', tone: '', title: 'Hardware and networks', text: 'Catalog, devices, modules, subnets and addresses. Every Openness attribute can be read and written.' },
      { icon: 'tag', tone: 'teal', title: 'Tags and data types', text: 'Tag tables, constants and UDTs in bulk: create, update and search by name or address across the whole PLC.' },
      { icon: 'shield', tone: 'success', title: 'Safe for the plant', text: 'Every destructive action needs explicit confirmation. A download never stops the CPU or re-initializes DBs without your permission.' },
    ],
  },
  how: {
    badge: 'How it works',
    title: 'Three steps and you are talking to your project.',
    steps: [
      { title: 'Install Talk2Tia', text: 'Download the Windows installer. You need TIA Portal V21 with Openness and your user in the “Siemens TIA Openness” group.' },
      { title: 'Connect your assistant', text: 'Add Talk2Tia as an MCP server in Claude Code or Claude Desktop: a single line of configuration.' },
      { title: 'Ask', text: 'The assistant attaches to the open TIA Portal instance. Confirm the Openness dialog and work in plain language.' },
    ],
    c1: '# 1. check the environment',
    c2: '# 2. register the server in Claude Code',
    c3: '# 3. optional: PLC and libraries only',
  },
  stats: { tools: 'MCP tools', toolsets: 'toolsets you can enable', formats: 'code formats: SCL, SIMATIC SD, XML', api: 'official Openness API' },
  toolsets: {
    badge: 'Catalog',
    title: 'Seven toolsets, enabled when you need them.',
    lead: 'Use only what you need: fewer tools mean faster, more precise answers.',
    all: `See all ${stats.tools} tools →`,
    allText: 'Names, descriptions and risk level of every tool.',
    items: [
      { id: 'portal', name: 'Session & project', count: 16, text: 'Attach to an open TIA Portal or start it headless; open, create, save, archive and retrieve projects.' },
      { id: 'plc', name: 'PLC program', count: 35, text: 'Blocks, groups, UDTs, tags, constants, external sources, cross references.' },
      { id: 'library', name: 'Libraries', count: 25, text: 'Global and project libraries, types, versions, master copies and synchronisation.' },
      { id: 'hardware', name: 'Hardware', count: 11, text: 'Device tree, catalog, modules, subnets and network nodes.' },
      { id: 'online', name: 'Online', count: 10, text: 'Compile, online/offline compare, connection and controlled download.' },
      { id: 'hmi', name: 'HMI', count: 5, text: 'WinCC Comfort/Advanced and Unified: screens, tags, export and import.' },
      { id: 'object', name: 'Object model', count: 5, text: 'Browse and edit any Openness object: practically complete coverage.' },
    ],
  },
  safety: {
    badge: 'Safety',
    title: 'Built for people who work on real plants',
    items: [
      'Explicit confirmation for deletes, overwrites and downloads',
      'The CPU is never stopped and DBs are never re-initialized without permission',
      'Changes stay unsaved until you decide',
      'All Openness calls go through one dedicated STA thread',
      'No Siemens component included or redistributed',
    ],
  },
  req: {
    badge: 'Requirements',
    title: 'What you need',
    items: [
      'Windows 10/11 x64',
      'TIA Portal V21 with the Openness option',
      'Your user in the local “Siemens TIA Openness” group',
      'An MCP client: Claude Code, Claude Desktop or another compatible one',
    ],
    latest: 'Download the latest version',
    all: 'All releases',
  },
  faq: {
    badge: 'FAQ',
    title: 'Frequently asked questions',
    items: [
      { q: 'Which AI clients can I use?', a: 'Any client that supports the Model Context Protocol: Claude Code, Claude Desktop and other MCP clients. Talk2Tia communicates over stdio.' },
      { q: 'Do I need an Openness licence?', a: 'You need TIA Portal V21 with the Openness option installed and your Windows user in the “Siemens TIA Openness” group. Talk2Tia neither includes nor redistributes Siemens components.' },
      { q: 'Are changes immediate?', a: 'Yes. Attached to an open instance, Talk2Tia works in the project in real time. Changes stay unsaved until you save them.' },
      { q: 'Can it harm the plant?', a: 'Destructive operations require explicit confirmation. Downloads to the CPU must always be approved, and stopping the CPU or re-initializing DBs must be authorized separately.' },
      { q: 'Can I limit the available tools?', a: 'Yes, with the TALK2TIA_TOOLSETS variable (for example “plc,library”). Fewer tools mean less context for the model and more focused answers.' },
      { q: 'Who is responsible for what Talk2Tia does?', a: 'You are. Talk2Tia is provided “as is”, without warranty of any kind. InsideApp S.r.l. accepts no liability for any damage, loss of data, production downtime, injury or other consequence arising from its use, including changes made by AI assistants to projects, PLC programs or plants. Every change must be reviewed, tested and approved by qualified personnel before it reaches a real machine. See the legal notice for the full terms.' },
    ],
  },
  cta: {
    title: 'Bringing AI to your automation team?',
    lead: 'InsideApp builds software for industry. Let’s talk about adapting Talk2Tia to your standards and libraries.',
    contact: 'Contact us',
    github: 'View on GitHub',
  },
  footer: {
    tagline: 'The MCP server that lets your AI assistant talk to TIA Portal V21 through the official Openness API.',
    product: 'Product',
    resources: 'Resources',
    catalog: 'Tool catalog',
    issue: 'Report an issue',
    legalLink: 'Legal notice',
    contact: 'Contact us',
    madeBy: 'A project by',
    legal: 'Released under the MIT licence. Siemens, SIMATIC and TIA Portal are trademarks of Siemens AG. Talk2Tia is not affiliated with Siemens AG.',
  },
  docs: {
    title: 'Documentation — Talk2Tia',
    description: 'Installation, MCP client configuration and first steps with Talk2Tia.',
    badge: 'Documentation',
    heading: 'Getting started',
    lead: 'Requirements, installation, MCP client configuration and how code editing works.',
  },
  tools: {
    title: 'Tool catalog — Talk2Tia',
    description: `The ${stats.tools} Talk2Tia MCP tools for TIA Portal V21, grouped by toolset.`,
    badge: 'Catalog',
    heading: `${stats.tools} tools, ${stats.toolsets} toolsets`,
    lead: 'The catalog is generated from the source code: names and descriptions are exactly what your AI assistant sees.',
  },
  legal: {
    title: 'Legal notice — Talk2Tia',
    description: 'Disclaimer of warranty and limitation of liability for Talk2Tia.',
    badge: 'Legal',
    heading: 'Legal notice',
    lead: 'Disclaimer of warranty and limitation of liability.',
  },
  notFound: {
    title: 'Page not found — Talk2Tia',
    heading: 'This rung is not wired.',
    lead: 'The page you are looking for does not exist or has moved.',
    home: 'Back to home',
  },
};

type Dict = typeof en;

const it: Dict = {
  beta: {
    label: 'Beta pubblica',
    text: 'Talk2Tia v0.9 è in beta pubblica: usalo su progetti di prova e aspettati qualche imperfezione.',
    link: 'Invia un feedback',
  },
  meta: {
    title: 'Talk2Tia — Parla con TIA Portal',
    description:
      'Server MCP per Siemens TIA Portal V21: il tuo assistente AI legge e scrive blocchi SCL e KOP, gestisce librerie, compila e scarica, tramite Openness.',
  },
  nav: {
    features: 'Funzionalità',
    how: 'Come funziona',
    tools: 'Tool',
    docs: 'Documentazione',
    download: 'Scarica',
    menu: 'Apri menu',
    theme: 'Cambia tema chiaro/scuro',
    language: 'Lingua',
    skip: 'Vai al contenuto',
  },
  hero: {
    badge: 'Server MCP per TIA Portal V21',
    titleA: 'Parla con',
    titleB: 'TIA Portal',
    titleC: 'Lui ti ascolta.',
    lead:
      "Talk2Tia collega il tuo assistente AI a TIA Portal tramite l'API ufficiale Openness. Chiedi in italiano, e lui scrive SCL e KOP, gestisce le librerie, compila e verifica, dentro il progetto che hai aperto.",
    ctaDownload: 'Scarica per Windows',
    ctaDocs: 'Inizia in 5 minuti',
    note: `Gratuito e open source (MIT) · ${stats.tools} tool · Windows x64 · TIA Portal V21`,
    demoLabel: 'Esempio di conversazione con Talk2Tia',
    demoUser: 'Aggiungi un controllo di inceppamento a FB_Conveyor: se il sensore resta coperto più di 5 s, ferma il motore.',
    demoCompile: 'plc_compile · 0 errori, 0 warning',
    demoBot: "Fatto. FB_Conveyor compila senza errori. Ho aggiunto il timer TON <code>JamTimer</code> e l'uscita <code>JamAlarm</code>. Vuoi che salvi il progetto?",
  },
  features: {
    badge: 'Funzionalità',
    title: 'Tutto TIA Portal, in linguaggio naturale.',
    lead: 'Dalla singola rete KOP alla libreria aziendale: Talk2Tia espone a un assistente AI ciò che fai ogni giorno in TIA Portal.',
    items: [
      { icon: 'code', tone: '', title: 'Scrive SCL e KOP', text: "Legge e modifica i blocchi in SCL come sorgente, e in KOP/FUP attraverso i documenti testuali SIMATIC SD. Per tutto il resto c'è SimaticML." },
      { icon: 'layers', tone: 'teal', title: 'Librerie, davvero', text: 'Tipi e versioni, edit e release, master copy, update di progetto e di libreria, harmonize e cleanup: gli stessi comandi di TIA Portal.' },
      { icon: 'check', tone: 'success', title: 'Compila e verifica', text: 'Compila hardware, software o un singolo blocco e restituisce errori e warning con la loro posizione, così l’assistente corregge da solo.' },
      { icon: 'cpu', tone: '', title: 'Hardware e reti', text: 'Catalogo, dispositivi, moduli, subnet e indirizzi. Ogni attributo Openness è leggibile e scrivibile.' },
      { icon: 'tag', tone: 'teal', title: 'Tag e tipi di dato', text: 'Tabelle tag, costanti e UDT in blocco: crea, aggiorna e cerca per nome o indirizzo in tutto il PLC.' },
      { icon: 'shield', tone: 'success', title: 'Sicuro per l’impianto', text: 'Ogni azione distruttiva richiede una conferma esplicita. Il download non ferma mai la CPU né reinizializza i DB senza il tuo permesso.' },
    ],
  },
  how: {
    badge: 'Come funziona',
    title: 'Tre passi e stai già parlando con il tuo progetto.',
    steps: [
      { title: 'Installa Talk2Tia', text: "Scarica l'installer per Windows. Ti serve TIA Portal V21 con Openness e l'utente nel gruppo “Siemens TIA Openness”." },
      { title: 'Collega il tuo assistente', text: 'Aggiungi Talk2Tia come server MCP in Claude Code o Claude Desktop: basta una riga di configurazione.' },
      { title: 'Chiedi', text: "L'assistente si collega all'istanza di TIA Portal aperta. Confermi il popup Openness e lavori in linguaggio naturale." },
    ],
    c1: "# 1. verifica l'ambiente",
    c2: '# 2. registra il server in Claude Code',
    c3: '# 3. opzionale: solo PLC e librerie',
  },
  stats: { tools: 'tool MCP', toolsets: 'gruppi attivabili', formats: 'formati di codice: SCL, SIMATIC SD, XML', api: 'API ufficiale Openness' },
  toolsets: {
    badge: 'Catalogo',
    title: 'Sette gruppi, attivabili quando servono.',
    lead: 'Usa solo quello che ti serve: meno tool significa risposte più veloci e più precise.',
    all: `Vedi tutti i ${stats.tools} tool →`,
    allText: 'Nomi, descrizioni e livello di rischio di ogni tool.',
    items: [
      { id: 'portal', name: 'Sessione e progetto', count: 16, text: 'Attach a TIA aperto o avvio headless, apri, crea, salva, archivia e recupera progetti.' },
      { id: 'plc', name: 'Programma PLC', count: 35, text: 'Blocchi, gruppi, UDT, tag, costanti, sorgenti esterne, riferimenti incrociati.' },
      { id: 'library', name: 'Librerie', count: 25, text: 'Librerie globali e di progetto, tipi, versioni, master copy e sincronizzazione.' },
      { id: 'hardware', name: 'Hardware', count: 11, text: 'Albero dispositivi, catalogo, moduli, subnet e nodi di rete.' },
      { id: 'online', name: 'Online', count: 10, text: 'Compilazione, confronto online/offline, connessione e download controllato.' },
      { id: 'hmi', name: 'HMI', count: 5, text: 'WinCC Comfort/Advanced e Unified: schermate, tag, export e import.' },
      { id: 'object', name: 'Object model', count: 5, text: 'Naviga e modifica qualunque oggetto Openness: copertura praticamente totale.' },
    ],
  },
  safety: {
    badge: 'Sicurezza',
    title: 'Pensato per chi lavora su impianti veri',
    items: [
      'Conferma esplicita per cancellazioni, sovrascritture e download',
      'La CPU non viene fermata e i DB non vengono reinizializzati senza autorizzazione',
      'Le modifiche restano non salvate finché non decidi tu',
      'Tutte le chiamate Openness passano da un unico thread STA dedicato',
      'Nessun componente Siemens incluso o ridistribuito',
    ],
  },
  req: {
    badge: 'Requisiti',
    title: 'Cosa ti serve',
    items: [
      'Windows 10/11 x64',
      "TIA Portal V21 con l'opzione Openness",
      'Utente nel gruppo locale “Siemens TIA Openness”',
      'Un client MCP: Claude Code, Claude Desktop o un altro compatibile',
    ],
    latest: "Scarica l'ultima versione",
    all: 'Tutte le release',
  },
  faq: {
    badge: 'FAQ',
    title: 'Domande frequenti',
    items: [
      { q: 'Quali client AI posso usare?', a: 'Qualunque client compatibile con il Model Context Protocol: Claude Code, Claude Desktop e gli altri client MCP. Talk2Tia comunica via stdio.' },
      { q: 'Serve una licenza Openness?', a: 'Serve TIA Portal V21 con l’opzione Openness installata e l’utente Windows nel gruppo “Siemens TIA Openness”. Talk2Tia non include né ridistribuisce componenti Siemens.' },
      { q: 'Le modifiche sono immediate?', a: 'Sì. Collegato a un’istanza aperta, Talk2Tia lavora nel progetto in tempo reale. Le modifiche restano non salvate finché non le salvi tu.' },
      { q: 'Può fare danni sull’impianto?', a: 'Le operazioni distruttive richiedono conferma esplicita. Il download verso la CPU va sempre approvato, e lo stop della CPU o la reinizializzazione dei DB vanno autorizzati a parte.' },
      { q: 'Posso limitare i tool disponibili?', a: 'Sì, con la variabile TALK2TIA_TOOLSETS (ad esempio “plc,library”). Meno tool significa meno contesto per il modello e risposte più mirate.' },
      { q: 'Chi è responsabile di ciò che fa Talk2Tia?', a: 'Tu. Talk2Tia è fornito “così com’è”, senza garanzie di alcun tipo. InsideApp S.r.l. non risponde di danni, perdite di dati, fermi produttivi, lesioni o altre conseguenze derivanti dal suo utilizzo, comprese le modifiche apportate da assistenti AI a progetti, programmi PLC o impianti. Ogni modifica deve essere verificata, collaudata e approvata da personale qualificato prima di arrivare su una macchina reale. I termini completi sono nelle note legali.' },
    ],
  },
  cta: {
    title: "Vuoi portare l'AI nel tuo reparto automazione?",
    lead: "InsideApp sviluppa software per l'industria. Parliamo di come Talk2Tia può adattarsi ai tuoi standard e alle tue librerie.",
    contact: 'Contattaci',
    github: 'Guarda su GitHub',
  },
  footer: {
    tagline: "Il server MCP che fa parlare il tuo assistente AI con TIA Portal V21, tramite l'API ufficiale Openness.",
    product: 'Prodotto',
    resources: 'Risorse',
    catalog: 'Catalogo tool',
    issue: 'Segnala un problema',
    legalLink: 'Note legali',
    contact: 'Contattaci',
    madeBy: 'Un progetto di',
    legal: 'Rilasciato con licenza MIT. Siemens, SIMATIC e TIA Portal sono marchi di Siemens AG. Talk2Tia non è affiliato a Siemens AG.',
  },
  docs: {
    title: 'Documentazione — Talk2Tia',
    description: 'Installazione, configurazione dei client MCP e primo utilizzo di Talk2Tia.',
    badge: 'Documentazione',
    heading: 'Per iniziare',
    lead: 'Requisiti, installazione, configurazione dei client MCP e funzionamento della modifica del codice.',
  },
  tools: {
    title: 'Catalogo tool — Talk2Tia',
    description: `I ${stats.tools} tool MCP di Talk2Tia per TIA Portal V21, divisi per gruppo.`,
    badge: 'Catalogo',
    heading: `${stats.tools} tool, ${stats.toolsets} gruppi`,
    lead: 'Il catalogo è generato dal codice sorgente: nomi e descrizioni (in inglese) sono quelli che vede il tuo assistente AI.',
  },
  legal: {
    title: 'Note legali — Talk2Tia',
    description: 'Esclusione di garanzia e limitazione di responsabilità di Talk2Tia.',
    badge: 'Legale',
    heading: 'Note legali',
    lead: 'Esclusione di garanzia e limitazione di responsabilità.',
  },
  notFound: {
    title: 'Pagina non trovata — Talk2Tia',
    heading: 'Questa rete non è cablata.',
    lead: 'La pagina che cerchi non esiste o è stata spostata.',
    home: 'Torna alla home',
  },
};

const dictionaries: Record<Lang, Dict> = { en, it };

export function t(lang: Lang): Dict {
  return dictionaries[lang];
}
