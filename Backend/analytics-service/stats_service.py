from flask import Flask, jsonify, request #Import per Flask, conversione in JSON e gestione delle richieste HTTP
from pymongo import MongoClient #Import per collegarmi al database MongoDB
import pandas as pd #Import per la manipolazione dei dati
import os #per gestire le operazioni nel sistema
from functools import lru_cache #decoratore per caching nella RAM
import time #gestione del tempo
from typing import Dict, Any, Optional #Per i Type Hints


#Inizializzo Flask e leggo dalle variabili d'ambiente l'URL di mongo DB e lo salvo
app = Flask(__name__)
MONGO_URI = os.getenv("MONGO_URI", "mongodb://localhost:27017")

class StatsService:


    # Definiamo le costanti a livello di classe invece che ricalcolarle nelle istanze.
    ms_in_a_hour = 3600 * 1000
    ms_in_a_day = 86400 * 1000

    #Costruttore della classe, passiamo se stesso e l'URL di MongoDB (che deve essere una stringa)
    def __init__(self, db_uri: str):
        print("Inizializzazione servizio statistiche")
        #Apro la connessione verso il DB
        self.client = MongoClient(db_uri)
        #Seleziono il database che mi interessa
        self.db = self.client.earthquake_db
        #e la tabella che mi interessa
        self.collection = self.db.events



    #Mi serve per tradurre il testo in un numero
    #es. "hour" in un valore in millisecondi
    def get_cutoff_time(self, range_param: str) -> int:
        #Prendo l'orario attuale in millisecondi arrotondato ad un intero
        now_ms = int(time.time() * 1000)
        #Sostituisco la catena if-elif-else con match-case (Python 3.10+)
        match range_param:
            case "hour":
                return now_ms - self.ms_in_a_hour
            case "day":
                return now_ms - self.ms_in_a_day
            case "7days":
                return now_ms - (7 * self.ms_in_a_day)
            case "30days":
                return now_ms - (30 * self.ms_in_a_day)
            case _:
                #Caso di default (equivalente all'else finale)
                #Nel caso in cui il filtro non dovesse funzionare 
                #dico di prendere tutto
                return 0

    #Metodo interno che traduce le richieste degli utenti in query per MongoDB
    #In ingresso prende una stringa che può contere il luogo, oppure no
    #e il timestamp da cui partire, ritorna una DataFrame di Pandas
    def _fetch_dataframe(self, place_filter: Optional[str] = None, cutoff_time: int = 0) -> pd.DataFrame:
        #Se l'utente non passa filtri, mando tutto
        query = {}

        #Uso $regex e $options:i così come nel backend, quindi se cerco ITA
        #in automatico torna "Italy"
        if place_filter:
            query["place"] = {"$regex": place_filter, "$options": "i"}

        #idem qui, il tempo deve essere maggiore o uguale a quello passato nel cutoff
        if cutoff_time > 0:
            query["time"] = {"$gte": cutoff_time}

        #Eseguo la query nascondendo l'ID (non mi interessa)
        cursor = self.collection.find(query, {"_id": 0})
        #Scarico i dati della query in un dizionario di "events"
        events = list(cursor)
        
        #Se non ci sono eventi, torno un DataFrame vuoto
        if not events:
            return pd.DataFrame()
        #Se sono presenti eventi, Pandas li trasforma da un dizionario in una tabella
        return pd.DataFrame(events)

    #Metodo per valutare il rischio, prende in input il valore massimo del magnitudo
    #e torna un dizionario chiave valore con il livello e il colore
    def assess_risk_details(self, max_mag: float) -> Dict[str, str]:
        if max_mag >= 6.0:
            return {"level": "CRITICO", "color_code": "#FF0000"}
        elif max_mag >= 4.5:
            return {"level": "ATTENZIONE", "color_code": "#FFA500"}
        elif max_mag >= 3.0:
            return {"level": "PRE-ALLERTA", "color_code": "#B8860B"}
        else:
            return {"level": "NORMALE", "color_code": "#008000"}


    #Trasforma i valori numeri in un testo che mi riassume la situazione sismica 
    def generate_summary_text(self, count: int, max_m: float, strongest_place: str, place_filter: Optional[str] = None, time_label: str = "Nel periodo selezionato") -> str:
        #Area viene impostato a "nella zona..." se il filtro del luogo c'è, altrimenti no
        #tramite un espressione condizionale (o operatore ternario)
        area = f"nella zona '{place_filter}'" if place_filter else "nel mondo"
        text = f"{time_label} sono stati analizzati {count} eventi {area}. "

        if count == 0:
            return f"Nessun evento sismico rilevato {area} {time_label.lower()}." #Lo converto in minuscolo

        if max_m > 5.5:
            text += f"ALLERTA: E' stato registrato un violento terremoto di magnitudo {max_m} a {strongest_place}. Si consiglia massima prudenza."
        elif max_m > 4.0:
            text += f"Attivita' sismica significativa, picco di magnitudo {max_m} presso {strongest_place}."
        else:
            text += "L'attivita' sismica e' bassa e rientra nei parametri di normalita'."

        return text

    #Metodo in cui avviene il reale calcolo delle statistiche 
    def calculate_stats(self, place_filter: Optional[str] = None, range_param: str = "day", time_label: str = "Nelle ultime 24 ore") -> Dict[str, Any]:
        
        #Traduciamo il testo in un timestamp
        cutoff = self.get_cutoff_time(range_param)
        #Scarichiamo i dati dal DB e li convertiamo in un DataFrame di Pandas
        df = self._fetch_dataframe(place_filter, cutoff)

        #Se non ci sono dati, non calcoliamo niente
        if df.empty:
            return {
                "total_events": 0, "max_magnitude": 0, "avg_magnitude": 0,
                "strongest_place": "N/A",
                "risk_status": {"level": "N/A", "color_code": "#808080"},
                "summary_text": f"Nessun dato disponibile {time_label.lower()}."
            }
        #Altrimenti calcoliamo i valori dove è avvenuto il terremoto più forte
        max_m = float(df["magnitude"].max())
        avg_m = float(round(df["magnitude"].mean(), 2))
        #Qui estraggo solo la riga del luogo in cui è avvenuto quello più forte (idxmax() mi torna l'indice di quella riga)
        strongest_place = df.loc[df["magnitude"].idxmax()]["place"]

        #Calcoliamo il rischio
        risk_info = self.assess_risk_details(max_m)
        #e generiamo il testo basandoci sui dati che sono tornati
        summary = self.generate_summary_text(len(df), max_m, strongest_place, place_filter, time_label)

        #Torniamo le statistiche generate e analizzate
        return {
            "total_events": int(len(df)),
            "max_magnitude": max_m,
            "avg_magnitude": avg_m,
            "strongest_place": strongest_place,
            "risk_status": risk_info,
            "summary_text": summary
        }
    
    
    #Usiamo il metodo lru_cache (Last Recently Used) dal modulo functools per velocizzare
    #le risposte del database nel caso di una query già calcolata inserendole in una cache
    #di dimensione 20 che non scade mai (TTL impostato a none) La cache si basa su 4 parametri
    #Tempo di richiesta, luogo, giorno, e l'etichetta temporale
    #Se un utente chiede gli stessi parametri (nel range della richiesta) questi vengono prelevati
    #dalla cache
    @lru_cache(maxsize=20)
    def calculate_stats_cached(self, ttl_hash: Optional[int] = None, place_filter: Optional[str] = None, range_param: str = "day", time_label: str = "") -> Dict[str, Any]:
        return self.calculate_stats(place_filter, range_param, time_label)



stats = StatsService(MONGO_URI)


#Punto d'ingresso delle statistiche tramite Flask
#Definisce l'endpoint in cui leggere (GET) le statistiche
@app.route('/api/stats', methods=['GET'])
def get_stats():


    try:
        #Leggo i parametri della richiesta
        place = request.args.get('place')
        range_param = request.args.get('range', 'day')

        #Traduco le etichette in testi per l'utente
        time_labels = {
            "hour": "Nell'ultima ora",
            "day": "Nelle ultime 24 ore",
            "7days": "Negli ultimi 7 giorni",
            "30days": "Negli ultimi 30 giorni"
        }
        #Se il parametro non è nel dizionario, torno una frase di default
        label = time_labels.get(range_param, "Nel periodo selezionato")

        #Aggiorniamo il tempo di generazione delle entry della cache ogni 10 secondi
        current_window = int(time.time() / 10)


        #Calcolo delle statistiche
        data = stats.calculate_stats_cached(
            ttl_hash=current_window,
            place_filter=place,
            range_param=range_param,
            time_label=label
        )
        #Se tutto va bene torniamo i dati con codice 200
        return jsonify(data), 200
    #Altrimenti solleviamo un eccezione
    except Exception as e:
        return jsonify({"Errore": str(e)}), 500

if __name__ == '__main__':
    print("Stats Service avviato sulla porta 5000")
    app.run(host='0.0.0.0', port=5000)