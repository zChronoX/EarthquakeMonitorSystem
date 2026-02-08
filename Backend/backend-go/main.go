package main

import (
	"backend-go/models" //Qui ho la definizio della struct "Earthquake"
	"bytes"             //Mi serve per manipolare slice di byte
	"context"           //Mi serve per gestire la concorrenza e i timeout
	"encoding/csv"      //Mi serve per leggere e scrivere i file CSV
	"encoding/json"     //Mi serve per la codifica e decodifica dei JSON
	"fmt"               //Pacchetto standard per l'I/O formattato
	"io"                //Pacchetto per le primitive dell'I/O
	"log"               //Pacchetto di base per il logging
	"math/rand"         //Generatore di numeri pseudo-casuali
	"net/http"          //Mi serve per le implementazioni client/server HTTP
	"os"                //Interfaccia verso l'SO
	"strconv"           //Mi serve per la conversione di stringhe in tipi base come float o interi
	"sync"              //Mi serve per la sincronizzazione della memoria
	"time"              //Mi serve per la gestione del tempo

	//Driver ufficiali del framework Gin
	"github.com/gin-gonic/gin"

	//Driver ufficiali per MongoDB
	"go.mongodb.org/mongo-driver/bson"          //E' il tipo di formato usato da Mongo per salvare i dati nel DB
	"go.mongodb.org/mongo-driver/mongo"         //Mi serve per connettermi al DB e gestirlo
	"go.mongodb.org/mongo-driver/mongo/options" //Mi serve per configurare le query
)

//DEFINIZIONE TIPI ED ENUMERAZIONI

// RiskLevel definisce il livello di rischio usando iota per le costanti
// Abbiamo usato il costrutto const con il generatore iota per creare
// sequenze di costanti tipizzate, al fine di migliorare la leggibilità.
type RiskLevel int

const (
	RiskLow      RiskLevel = iota // 0
	RiskModerate                  // 1
	RiskHigh                      // 2
	RiskCritical                  // 3
)

// Implementazione di Stringer per permettere
// di avere una rappresentazione testuale dell'enum quando viene stampato.
func (r RiskLevel) String() string {
	return [...]string{"LOW", "MODERATE", "HIGH", "CRITICAL"}[r]
}

// Funzione per il calcolo della logica di business basato
// sull'intensità del magnitudo
func CalculateRisk(mag float64) RiskLevel {
	switch {
	case mag >= 6.0:
		return RiskCritical
	case mag >= 4.5:
		return RiskHigh
	case mag >= 2.5:
		return RiskModerate
	default:
		return RiskLow
	}
}

//INTERFACCE E DISACCOPPIAMENTO

//Definiamo un'interfaccia per lo store.
//Questo ci permette di disaccoppiare la logica di business dal DB specifico (MongoDB).
//Utile per per cambiare tecnologia senza rifare tutto il codice.

// EventStore definisce il contratto
// Significa che chiunque implementi questa interfaccia
// deve conoscere (da contratto) i metodi definiti al suo interno
type EventStore interface {
	Upsert(ctx context.Context, event models.Earthquake) error
	Query(ctx context.Context, filter interface{}, limit int64) ([]models.Earthquake, error)
	DeleteOld(ctx context.Context, cutoffTime int64) (int64, error)
	GetAll(ctx context.Context) ([]models.Earthquake, error)
}

// MongoStore è l'implementazione concreta di EventStore per MongoDB
type MongoStore struct {
	collection *mongo.Collection
}

//Implementazione dei metodi definiti nell'interfaccia sopra

//Questa implementa la logica di inserimento e aggiornamento nel DB
//in pratica mi fa aggiungere nuovi elementi, o modifica quelli presenti
//se hanno un'ID corrispondente

// Usiamo un Pointer Receiver (m *MongoStore) per evitare la copia della struct
// ad ogni chiamata, anche se non modifichiamo i campi interni della struct MongoStore
func (m *MongoStore) Upsert(ctx context.Context, event models.Earthquake) error {

	//Qui sto cercando un elemento che abbia l'ID uguale a quello passato nella funzione
	filter := bson.M{"_id": event.ID}

	//Questa mi serve per usare l'Upsert
	//Senza questa riga, se l'evento non ci fosse nel DB, l'operazione fallirebbe
	opts := options.Replace().SetUpsert(true)

	//Eseguo l'operazione, non mi interessa il primo valore di ritorno
	//Ma il secondo si, che sarebbe il caso in cui torna errore
	_, err := m.collection.ReplaceOne(ctx, filter, event, opts)
	return err
}

// Funzione che si occupa di recuperare la lista dei terremoti dal database
func (m *MongoStore) Query(ctx context.Context, filter interface{}, limit int64) ([]models.Earthquake, error) {

	//Imposto l'ordinamento dal più nuovo al più vecchio
	opts := options.Find().SetSort(bson.D{{Key: "time", Value: -1}})
	if limit > 0 {
		//Imposto il limite di elementi che voglio ricevere nella chiamata
		opts.SetLimit(limit)
	}
	//Eseguo la chiamata al DB. Il valore di ritorno non sono i dati
	//Ma un cursor, ovvero il puntatore al flusso di dati sul database.
	cursor, err := m.collection.Find(ctx, filter, opts)
	if err != nil {
		return nil, err
	}
	//Utilizzo del Defer per assicurarci che
	//il cursore venga chiuso alla fine della funzione,
	//prevenendo memory leak anche i caso di errore.
	//Quindi posticipo la chiusura fino al momento
	//in cui la funzione Query termina
	defer cursor.Close(ctx)

	//Qui avviene lo scorrimento di tutto il cursore
	//in cui scarichiamo e convertiamo i dati
	//nella variabile events Earthquake
	var events []models.Earthquake

	//Passiamo il puntatore alla slice perché
	//deve poter modificare la variabile events
	if err = cursor.All(ctx, &events); err != nil {
		return nil, err
	}
	//Per evitare errori nel frontend (quindi valori nulli)
	//Restituisce slice vuota invece di nil nel caso in cui
	//non ci siano dati
	if events == nil {
		events = []models.Earthquake{}
	}
	return events, nil
}

// Questa mi serve per pulire il DB dai valori vecchi o simulati (finti)
// Tutti gli eventi precedenti al "cutoffTime" sono considerati vecchi
// e vengono cancellati. Mi torna il numero di elementi cancellati
// (e come negli altri casi, un'errore (eventualmente))
func (m *MongoStore) DeleteOld(ctx context.Context, cutoffTime int64) (int64, error) {

	//Qui sto costruendo il filtro della query, in particolare
	//Vogliamo cancellare i dati che sono precedenti al cutoffTime
	//Ma anche quelli simulati, con un doppio controllo
	//Sia se il flag "is_simulated" è true, ma anche se l'ID
	//di quella entry inizia con "sim_"
	filter := bson.M{
		"$or": []bson.M{
			{"time": bson.M{"$lt": cutoffTime}},
			{"is_simulated": true},
			{"_id": bson.M{"$regex": "^sim_"}},
		},
	}
	//Eseguiamo la query con il filtro
	result, err := m.collection.DeleteMany(ctx, filter)
	if err != nil {
		//Ignoro il primo valore di ritorno
		//e torno l'errore nel caso di fallimento
		return 0, err
	}
	//Torniamo il numero di elementi eliminati
	//o niente (nil) nel caso in cui non c'è
	//nulla da cancellare
	return result.DeletedCount, nil
}

//Funzione che mi restituisce tutto il contenuto del DB senza limiti

func (m *MongoStore) GetAll(ctx context.Context) ([]models.Earthquake, error) {
	return m.Query(ctx, bson.M{}, 0)
}

//SCOPE E STRUTTURAZIONE
//Invece di usare variabili globali, incapsuliamo lo stato
//dell'applicazione in una struct.

// Abbiamo tutto il cuore dell'applicazione qui,
// l'EventStore analizzato sopra, l'EventChannel
// che serve per collegare le API ai Worker
// e il sincronizzatore che tiene conto
// di quante Gorutine sono attive (funziona come un contatore, è un puntatore
// essendo un oggetto condiviso, non va mai passato per valore)
type App struct {
	Store        EventStore
	EventChannel chan models.Earthquake
	WG           *sync.WaitGroup
}

//MAIN

func main() {

	//Configurazione del database
	mongoURI := os.Getenv("MONGO_URI")
	if mongoURI == "" {
		mongoURI = "mongodb://localhost:27017"
	}

	//Usiamo un context con timeout per evitare che l'applicazione si blocchi
	//all'infinito in fase di avvio se il DB non risponde. Invece che rimanere
	//bloccata in attesa del DB, dopo 10 secondi fallisce.
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)

	//Il defer qui mi permette di liberare le risorse quando il main termina
	defer cancel()

	//Connesione al DB
	client, err := mongo.Connect(ctx, options.Client().ApplyURI(mongoURI))
	if err != nil {
		log.Fatal(err)
	}

	//
	dbCollection := client.Database("earthquake_db").Collection("events")
	log.Println("Connesso a MongoDB")

	//Inizializzazione App e Dipendenze
	//Iniettiamo &MongoStore nel campo Store, in modo tale che
	//l'applicazione può usare i metodi astratti dell'interfaccia
	//EventStore
	app := &App{
		Store: &MongoStore{collection: dbCollection},
		//Utilizzo dei Canali:
		//Creiamo un canale con buffer 100 per disaccoppiare parzialmente
		//produttore (API) e consumatore (Worker).
		//In modo che le APi possono accettare fino ad un massimo di 100
		//richieste anche se i worker sono occupati.
		EventChannel: make(chan models.Earthquake, 100),
		WG:           &sync.WaitGroup{},
	}

	//Invece di una singola goroutine, ne avviamo 10 per parallelizzare il lavoro. (Pool Workers)
	//Nel caso in cui una singola richiesta HTTP potrebbe saturare il sistema
	//Essendo molto leggere e soprattuto velocissime, evitiamo di avere colli di bottiglia
	//in Go, quando decidiamo di richiedere i dati dei terremoti delle scorse 24 ore, o di 7/30gg fa.

	numWorkers := 10
	for i := 0; i < numWorkers; i++ {
		app.WG.Add(1) //Incremento il contatore del WaitGroup prima di lanciare la Gorutine
		//Lancio la funzione startWorker in una nuova Gorutine
		go app.startWorker(i, app.EventChannel) //Usiamo la keyword "go" per avviare una goroutine
	}

	//Setup Gin
	//Gin è uno dei framework più utilizzati per il linguaggio Go
	//che ci ha semplificato molto il lavoro per gestire le API REST.
	r := gin.New()

	//Definizione Rotte usando i metodi dell'istanza App
	api := r.Group("/api")
	{
		//Passiamo i metodi dell'istanza 'app' come handler
		api.POST("/ingest", app.ingestEarthquake)
		api.GET("/events", app.getEvents)
		api.POST("/fetch-now", app.ManualFetch)
		api.POST("/simulate", app.simulateUSEarthquake)
		api.DELETE("/cleanup", app.cleanupOldEvents)
		api.GET("/export", app.exportCSV)
	}

	//Il main si ferma qui, ed entra in un loop infinito che gli permette
	//Di ascoltare le richieste HTTP sulla porta 8080. Rimane bloccato
	//Perché la Run è un'istruzione bloccante.
	log.Printf("Server Go avviato sulla porta 8080")
	r.Run(":8080")
}

// Logica dei Worker
// startWorker consuma gli eventi del canale
// Abbiamo usato un canale unidirezione (<-chan) per evitare
// che il worker scriva nel canale (visto che consuma solo).
// Banalmente ci serve per salvare i dati senza rallentare le rispost del client
func (app *App) startWorker(id int, events <-chan models.Earthquake) {

	//Quando la funzione termina, il Defer chiama Done() per segnalare che il worker ha finito il lavoro
	defer app.WG.Done() // Segnala al WaitGroup quando finito: Decrementa il contatore quando il worker finisce
	log.Printf("Worker %d avviato", id)

	//Ciclo for-range su un canale per far si che nel caso in cui questo sia vuoto
	//i worker vadano in "stand-by", cioè si addormentano finché non arriva un evento,
	//in modo da non consumare CPU.
	for event := range events {

		//Usiamo il metodo Upsert e se qualcosa non va come dovrebbe, il worker non viene fermato
		//Usiamo context.Background() perché il worker è un processo asincrono
		//e non deve dipendere dal contesto della richiesta HTTP originale (che è già terminata).
		//Significa che le richieste HTTP che riceviamo hanno il loro Context, che scade appena
		//inviamo la risposta al client, il worker però elabora l'evento (cioè la richiesta) dopo
		//che la risposta è già stata inviata (in modo asincrono). Se non facesse così, il salvataggio
		//sul database fallirebbe perché la richiesta originale è fallita (context scaduto)
		if err := app.Store.Upsert(context.Background(), event); err != nil {
			log.Printf("- Worker %d - Errore DB: %v", id, err)
		}
	}
}

//HANDLERS DELLE FUNZIONI CHIAMATE TRAMITE API

// Riceve i dati dall'API esterna e li inserisce nel canale per essere consumati dai worker
func (app *App) ingestEarthquake(c *gin.Context) {
	var event models.Earthquake

	//Qui viene usata la reflection, mappiamo i campi del JSON in quelli della struct event
	if err := c.ShouldBindJSON(&event); err != nil {
		//Nel caso in cui il client inviasse dati non validi, la funzione si ferma
		//rispondendo con un errore.
		c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
		return
	}

	//Il select permette di gestire operazioni su canali.
	//Nel caso in cui ci sia spazio nel canale (100 slot), la richiesta viene messa in coda
	//altrimenti viene scartata perché non c'è spazio
	select {
	case app.EventChannel <- event:
		c.JSON(http.StatusOK, gin.H{"status": "queued"})
	default:
		//se il sistema è saturo, rifiutiamo la richiesta.
		c.JSON(http.StatusServiceUnavailable, gin.H{"status": "queue_full"})
	}
}

// Questa è una funzione di ricerca del database
func (app *App) getEvents(c *gin.Context) {

	//Leggo gli input passati nella query
	//quindi il magnitudo, luogo e il limite di valori
	minMagStr := c.Query("min_mag")
	placeFilter := c.Query("place")
	limitStr := c.Query("limit")

	//E costruisco il classico filtro che MoongoDB capisce
	filter := bson.M{}

	//Qui imposto il magnitudo minimo usando l'operatore $gte, cioè
	//Grather than or Equal (maggiore o uguale)
	if minMagStr != "" {
		if mag, err := strconv.ParseFloat(minMagStr, 64); err == nil {
			filter["magnitude"] = bson.M{"$gte": mag}
		}
	} else {
		//Nel caso in cui la query sia sprovvista di magnitudo
		//lo imposto a 0.
		filter["magnitude"] = bson.M{"$gte": 0.0}
	}
	//Qui imposto il luogo: uso $regex per cercare pezzi di testo
	//es. Texas viene trovato anche se scrivo Tex
	//e anche $options: "i" per il Case sensitive.
	//anche se scrivo TEXAS, la ricerca va a buon fine
	if placeFilter != "" {
		filter["place"] = bson.M{"$regex": placeFilter, "$options": "i"}
	}

	//Imposto il numero di valori che voglio ricevere
	var limit int64 = 0
	//Se l'utente non lo specifica nella richiesta, di default rimane 0 (cioè tutti i valori trovati)
	if limitStr != "" {
		//Altrimenti li devo convertire, da stringa ad interno e intero positivo
		//Non permetto all'utente di inserire un limite che non sia un numero
		//e soprattuto che non sia positivo. In caso contrario ignoro il parametro
		if l, err := strconv.ParseInt(limitStr, 10, 64); err == nil && l > 0 {
			limit = l
		}
	}

	//Passiamo c.Request.Context() allo store. Se l'utente annulla la richiesta
	//MongoDB interromperà l'operazione risparmiando risorse. Nel caso in cui
	//ad esempio, la richiesta è troppo lenta per l'utente e questo l'annulla.
	events, err := app.Store.Query(c.Request.Context(), filter, limit)
	if err != nil {
		c.JSON(500, gin.H{"Errore nel DB": "Non sono riuscito a connettermi"})
		return
	}
	//Se tutto va bene torniamo la lista di struct events in formato JSON
	//e la mandiamo al client.
	c.JSON(200, events)
}

// Funzione per il fetch manuale
// Abbiamo introdotto un principio di separazione delle resposabilità
// Il main (Go) non si occupa di scaricare i dati, ma solo di gestirli
// quindi servirli, inserirli nel DB.
// Colui che scarica i dati è il Sensor Agent (cioè Python)
// che nel caso in cui crashasse, o si bloccasse, il main rimane comunque
// attivo e può servire i dati (più vecchi) agli utenti.
func (app *App) ManualFetch(c *gin.Context) {
	//Definisco una struct di tipo Request
	var reqBody struct {
		Range string `json:"range"`
	}
	c.ShouldBindJSON(&reqBody)

	//Se l'utente non specifica il tempo, viene impostato di default all'ultima ora
	if reqBody.Range == "" {
		reqBody.Range = "hour"
	}

	//Trasformo la struct di Go in una sequenza di byte JSON
	jsonData, _ := json.Marshal(reqBody)
	//Delego il sensor_agent (Python) per scaricare i dati internet
	sensorURL := "http://sensor-agent:5001/trigger-fetch"

	//Qui uso la libreria standard net/http come Client
	//Anche qui propaghiamo il contesto per gestire timeout e cancellazioni
	//Legando la chiamata interna (dal main al sensor_agent) a quella esterna
	//. (dall'utente al main)
	req, _ := http.NewRequestWithContext(c.Request.Context(), "POST", sensorURL, bytes.NewBuffer(jsonData))
	req.Header.Set("Content-Type", "application/json")

	client := &http.Client{}
	resp, err := client.Do(req)

	//Controlliamo che la chiamata sia andata a buon fine
	if err != nil {
		log.Printf("Errore chiamata sensor-agent: %v", err)
		c.JSON(503, gin.H{"Errore": "Sensor agent non disponibile"})
		return
	}
	//Chiudiamo la connessione solo nel caso in cui non c'è errore
	//quindi prima delle fine della funzione ManualFetch
	defer resp.Body.Close()

	//Leggiamo cosa ha risposta il sensor_agent e lo inviamo all'utente.
	body, _ := io.ReadAll(resp.Body)
	c.Data(resp.StatusCode, "application/json", body)
}

// Questa è la funzione di pulizia del DB
// elimina tutto ciò che sià più vecchio di 1 ora e che non sia reale
// (quindi generato da noi)
func (app *App) cleanupOldEvents(c *gin.Context) {

	//Possiamo modificare da qui il tempo di cancellazione
	//o dalla richiesta che facciamo se lo specifichiamo
	hoursStr := c.Query("hours")
	hours := 1

	//Qui controlliamo come prima che nella query vengano passati dati corretti
	//quindi numeri interi positivi
	if hoursStr != "" {
		if h, err := strconv.Atoi(hoursStr); err == nil && h > 0 {
			hours = h
		}
	}

	//Qui definisco il tempo, misuro dal valore attuale, sottraggo 1 ora, e poi
	//converto il valore in millisecondi
	//tutto ciò che ha un timestamp minore del valore cutoffTime è considerato vecchio
	//e verrà cancellato
	cutoffTime := time.Now().Add(-time.Duration(hours) * time.Hour).UnixMilli()

	//Deleghiamo all'interfaccia Store la logica di cancellazione
	//noi specifichiamo solo i filtri della cancellazione ma la funzione
	//non sa come cancellare i dati dal DB.
	//Inoltre torniamo il numero di valori cancellati
	count, err := app.Store.DeleteOld(c.Request.Context(), cutoffTime)
	if err != nil {
		c.JSON(500, gin.H{"Errore DB": "Errore nella pulizia del database"})
		return
	}

	//Torniamo il numero di errori cancellati nell'arco di tempo specificato
	msg := fmt.Sprintf("Pulizia: rimossi %d eventi nelle ultime %d ore.", count, hours)
	log.Printf("PULIZIA %s", msg)

	//Torno un JSON che mi dice quanti elementi sono stati cancellati
	c.JSON(200, gin.H{
		"message":       msg,
		"deleted_count": count,
	})
}

// Questa funzione genera dei dati falsi, ci serve per popolare
// il database con dati che hanno magnitudi molto elevati
func (app *App) simulateUSEarthquake(c *gin.Context) {

	//Creo una stringa univoca che concatena "sim_" con il timestamp di ora
	//"sim_us" viene utilizzato nella DeleteOld nel MongoStore per conferma
	//che stiamo cancellando terremoti falsi
	fakeID := fmt.Sprintf("sim_us_%d", time.Now().UnixNano())
	//Qui abbiamo creato un array di città
	cities := []string{"San Francisco, CA", "Los Angeles, CA", "Seattle, WA", "Portland, OR", "San Diego, CA"}
	//Prelevo l'indice con un numero a caso tra 0 e n-1 (in base alla dimensione dell'array)
	randomCity := cities[rand.Intn(len(cities))]

	//Qui genero un numero float random compreso in un intervallo massimo e minimo (es. 48-32))
	//Sarebbero le coordinate false del terremoto che coprono la West Coast degli USA
	lat := 32.0 + rand.Float64()*(48.0-32.0)
	lon := -124.0 + rand.Float64()*(-115.0-(-124.0))
	//Qui generiamo un magnitudo minimo tra 5 e 9
	mag := 5.0 + rand.Float64()*4.0

	//Assemblo l'oggetto terremoto impostando il tag simulato a True
	fakeEvent := models.Earthquake{
		ID:          fakeID,
		Place:       fmt.Sprintf("SIMULATION: %s", randomCity),
		Magnitude:   mag,
		Time:        time.Now().UnixMilli(),
		Coordinates: []float64{lon, lat, 10.0},
		IsSimulated: true,
	}

	//Bypasso i worker e effettuo direttamente l'inserimento
	//Lo posso fare perché è un'azione che viene fatta dall'utente nel frontend
	//Non genero simultaneamente 1000 terremoti, quindi non ho bisogno di una coda di worker.
	if err := app.Store.Upsert(c.Request.Context(), fakeEvent); err != nil {
		c.JSON(500, gin.H{"error": "Failed to simulate"})
		return
	}
	c.JSON(201, fakeEvent)
}

// Questa funzione permette all'utente di scaricare un file CSV (eseguibile con Excel)
// che contiene tutti i dati presenti del database.
func (app *App) exportCSV(c *gin.Context) {

	//Intanto recupero tutti i dati dal DB
	events, err := app.Store.GetAll(c.Request.Context())
	if err != nil {
		c.JSON(500, gin.H{"error": "db error"})
		return
	}

	//Qui indico al browser di non mostrare il contenuto della finestra, ma solo quello di salvare
	//il file .csv, specificando che il formato è un testo separato da virgole
	c.Header("Content-Disposition", "attachment; filename=report_terremoti.csv")
	c.Header("Content-Type", "text/csv")

	//Qui facciamo in modo che la libreria CSV di Go si colleghi direttamente all'utente
	writer := csv.NewWriter(c.Writer)

	//Intestazione del file CSV
	writer.Write([]string{"Data Ora", "Luogo", "Magnitudo", "Profondita (km)", "Tsunami", "Rischio"})

	//Qui ignoro l'ID del terremoto
	for _, ev := range events {

		//Converto il dato da millisecondi (Timestamp UNIX) ad un formato comprensibile
		tm := time.UnixMilli(ev.Time).UTC()
		dateStr := tm.Format("2006-01-02 15:04:05.000")

		//Qui mi serve estrarre la profondita del terremoto
		depth := "0"
		//Visto che le coordinate sono salvate nel formato: latitudine, longitudine e pronfondità
		//controllo che l'array abbia veramente 3 elementi
		if len(ev.Coordinates) >= 3 {
			depth = fmt.Sprintf("%.1f", ev.Coordinates[2])
		}

		//Come sopra, mi interessa il rischio di tsunami
		tsunami := "NO"
		if ev.Tsunami > 0 {
			tsunami = "SI"
		}

		//Qui calcolo il rischio, convertendo il valore del magnitudo in una costante (Enum)
		risk := CalculateRisk(ev.Magnitude)

		//Qui scrivo riga per riga il file CSV
		writer.Write([]string{
			dateStr,
			ev.Place,
			fmt.Sprintf("%.2f", ev.Magnitude),
			depth,
			tsunami,
			risk.String(), //Chiama il metodo String() definito all'inizio
		})
	}
	//Obbligo a prendere i dati nella RAM e scriverli nella parte finale del file
	//senza Flush() le ultime righe del CSV andrebbero perse
	writer.Flush()
}
