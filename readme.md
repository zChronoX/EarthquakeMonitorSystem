# Earthquake Monitor System

## Descrizione del Progetto
Questo sistema è un'applicazione distribuita per il monitoraggio, l'archiviazione e l'analisi in tempo reale dei dati sismici globali forniti dall'USGS (United States Geological Survey).
L'architettura è basata su microservizi containerizzati tramite Docker per il backend e un'applicazione Desktop WPF (Windows Presentation Foundation) (.NET) per il frontend.

Il sistema permette di:
* Scaricare dati sismici in tempo reale o su intervalli storici.
* Archiviare i dati in modo persistente su MongoDB.
* Effettuare analisi statistiche avanzate (Rischio, Medie, Zone critiche).
* Esportare report in formato CSV direttamente dallo stream dati.

## Architettura del Sistema

Il sistema è suddiviso in 4 componenti principali:

1.  **Backend Core (Go):** Il cuore del sistema. Gestisce la persistenza su MongoDB, espone le API per il client, gestisce l'export CSV ad alte prestazioni e coordina i worker per il salvataggio concorrente dei dati.
2.  **Sensor Agent (Python):** Servizio di ingestion. Scarica i dati dall'USGS periodicamente (scheduler) o su richiesta manuale e li invia al Backend Go.
3.  **Analytics Service (Python):** Motore di calcolo. Esegue analisi statistiche (con Pandas) sui dati storici e valuta i livelli di rischio con codifica cromatica (Rosso/Arancio/Oro/Verde).
4.  **Frontend (C# WPF):** Dashboard interattiva per la visualizzazione dei dati, grafici, simulazioni e gestione del sistema.

---

## Guida all'Installazione e Avvio

### Prerequisiti
* Docker Desktop (con Docker Compose) installato e attivo.
* Visual Studio 2022 (o versioni successive) con supporto per .NET Desktop Development.


### 1. Avvio del Backend (Server)
Tutti i servizi lato server risiedono nella cartella Backend e sono orchestrati da Docker.

1.  Aprire il terminale nella cartella Backend.
2.  Eseguire il comando per costruire e avviare i container:

    ```bash
    docker-compose up --build
    ```

3.  Attendere che i log confermino l'avvio dei servizi:
    * `earthquake-go`: Server avviato sulla porta 8080.
    * `earthquake-analytics`: Stats Service avviato sulla porta 5000.
    * `earthquake-sensor`: Sensor API in ascolto sulla porta 5001.
    * `earthquake-mongo`: Container del database MongoDB.

### 2. Avvio del Frontend (Client)
Il client è un'applicazione nativa Windows situata nella cartella Frontend.

1.  Aprire la soluzione EarthquakeMonitor.sln in Visual Studio.
2.  Assicurarsi che il progetto EarthquakeMonitor sia impostato come StartUp Project (tasto destro sul progetto -> Set as Startup Project).
3.  Premere F5 (o il tasto Start) per avviare l'applicazione.

---

## Documentazione API REST

Il sistema espone API su diverse porte, gestite dai rispettivi microservizi.

### 1. Backend Core (Go) 
Entry-point principale per il Frontend e gateway per il database.

| Metodo | Endpoint | Parametri (Query/Body) | Descrizione |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/events` | `min_mag`, `place`, `limit` | Restituisce la lista dei terremoti filtrati dal DB MongoDB. |
| `POST` | `/api/ingest` | Body: JSON (Modello Earthquake) | Riceve un evento sismico e lo salva nel DB (Upsert). Usato dal Sensor Agent. |
| `POST` | `/api/fetch-now` | Body: `{"range": "hour"}` | Ordina al Sensor Agent di scaricare immediatamente nuovi dati. |
| `POST` | `/api/simulate` | - | Genera un terremoto simulato (Fake Data) sulla West Coast USA per testare gli alert. |
| `GET` | `/api/export` | - | Genera e scarica uno stream CSV dei dati attuali nel DB. |
| `DELETE`| `/api/cleanup` | Query: `hours` (opzionale) | Rimuove i dati simulati e quelli reali più vecchi di N ore. |

### 2. Analytics Service (Python) 
Servizio di calcolo statistico e analisi del rischio.

| Metodo | Endpoint | Parametri Query | Descrizione |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/stats` | `place`, `range` | Restituisce statistiche aggregate: totale eventi, magnitudo max/media, luogo più colpito e livello di rischio (con codice colore). |

### 3. Sensor Agent (Python) 
Servizio worker per l'acquisizione dati esterna.

| Metodo | Endpoint | Body (JSON) | Descrizione |
| :--- | :--- | :--- | :--- |
| `POST` | `/trigger-fetch` | `{"range": "hour/day/..."}` | Trigger manuale. Scarica i dati dall'URL USGS corrispondente e li invia al Backend Go. |

---

## Struttura del Frontend (C#)

Il progetto C# è organizzato secondo il pattern di separazione delle responsabilità:

* MainWindow.xaml / .cs: Logica UI principale, gestione della griglia dati, filtri e visualizzazione stati (colori rischio).
* GraphsWindow.xaml / .cs: Finestra dedicata alla visualizzazione dei grafici.
* DataService.cs: Classe "Service Layer" che incapsula tutte le chiamate HTTP verso Go e Python.
* Earthquake.cs: Modello dati (DTO) e definizioni delle classi statistiche (StatsData, RiskInfo).
* MagnitudeToColorConverter.cs: Classe di conversione per la formattazione condizionale (colori) delle righe nella griglia XAML.

---

##  Autori

* Giovanni Maria Contarino, Matricola 1000007029
* Alessia Provvidenza Tomarchio, Matricola 1000005160
"# EarthquakeMonitorSystem" 
