import time #gestione del tempo
import requests #gestione delle chiamate HTTP
import schedule #gestione della schedulazione dei task
import os #per interaggire con l'SO
import threading #gestioned ella concorrenza
from flask import Flask, jsonify, request
from typing import Dict, Any, List, Optional  #import per i "type hints"



#Inizializzazione di Flask
app = Flask(__name__)



#Agente per l'ingestione di dati sismici da USGS e l'inoltro a un backend Go.
#Adotta la logica ETL, Extract Transform e Load. Si occupa infatti di collegarsi
#all'API esterno di USGS tramite le request, scaricare i JSON, pulirli e inviarli
#al backend (Go)

class SeismicSensorAgent:




    #Inizializza la classe SensorAgent
    #E' il nostro costruttore che inizializza lo stato di una nuova istanza della classe
    #Necessita dell'URL del backend (Go)
    def __init__(self, backend_url: str) -> None:

        #URL Backend
        self.backend_url: str = backend_url

        #Dizionario degli URL USGS mappato come attributo 
        #E' la mappa di configurazione delle finestre temporali ai rispettivi endpoint di USGS
        self.usgs_urls: Dict[str, str] = {
            "hour": "https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_hour.geojson",
            "day": "https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_day.geojson",
            "7days": "https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_week.geojson",
            "30days": "https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_month.geojson"
        }

    #Scarica i dati dall'URL specificato e li invia al backend
    #Necessita dell'URL dell'API e di una label (stringa) che indica il tipo di fetch
    #torna il numero di eventi inviati al backend
    def fetch_and_send(self, url: str, mode_label: str) -> int:

        print(f"Download: {mode_label} , da USGS iniziato")

        #Usiamo una Session per permetterci di riutilizzare la stessa connessione nel
        #caso in cui faccio più richieste verso lo stesso host, migliorando le prestazioni
        #Usiamo anche un Context Manager che garantisce la chiusura della sessione
        #e il "free" delle risorse alla fine del blocco with (anche in caso di errori)
        with requests.Session() as session:
            try:
                #Proviamo a contattare l'URL di USGS con un timeout di 10 secondi
                response = session.get(url, timeout=10)
                #Se la richiesta non va a buon fine (solleva un'eccezione, tipo 404,429 o 500)
                #saltiamo al blocco except finale
                response.raise_for_status() 

                #Decodifichiamo la risposta in un dizionario Python con chiavi strighe e valori generici
                #la risposta, che viene restituita come stringa, viene convertita in un JSON
                data: Dict[str, Any] = response.json()
                
                #Estraiamo dal dizionario, la lista dei terremoti. Se la chiave "features" non c'è
                #torna una lista vuota
                features: List[Dict[str, Any]] = data.get("features", [])
                
                
                print(f"Fetch {mode_label} : Trovati {len(features)} eventi.")

                count = 0
                #Cicliamo su tutti i terremoti della lista
                for feature in features:
                    #Proviamo a processare, gestiamo l'errore se i dati sono malformati.
                    try:
                        #DA COMMENTARE QUI
                        processed_event = self._process_feature(feature)
                        #Se l'evento è valido lo mando al backend e incremento il contatore
                        if processed_event:
                            self._send_to_backend(session, processed_event)
                            count += 1
                    except (ValueError, KeyError) as e:
                        #Se nel processamento di un terremoto mancano dati o non sono completi
                        #passiamo al prossimo invece di interrompere il fetch
                        print(f"Errore processamento evento {feature.get('id', '?')}: {e}")

                return count

            #Qui catturo gli errori di rete (generati da session o raise_for_status
            except requests.RequestException as e:
                print(f"Errore di rete durante il fetch {mode_label}: {e}")
                return 0
            except Exception as e:
                print(f"Errore imprevisto in fetch_and_send: {e}")
                return 0


    #Metodo che mi serve per processare (la T di Transform) i dati che vengono da USGS
    #Accetta in ingresso i terremoti singoli e ritorna un dizionario con i dati puliti
    #oppure i dati non validi/processabili (verranno scartati dalla funzione sopra)
    def _process_feature(self, feature: Dict[str, Any]) -> Optional[Dict[str, Any]]:

        #I valori del GeoJSON che ci interessano sono "properties", cioè tutti i valori
        #del terremoto, e "geometry", cioè coordinate e profondità dell'evento
        props = feature.get("properties", {})
        geometry = feature.get("geometry", {})

        #Se non ci sono questi dati dal terremoto in ingresso, solleviamo un'eccezione 
        if not props or not geometry:
            raise ValueError("Dati evento incompleti (mancano properties o geometry)")

        #Qui creiamo il dizionario di ritorno
        return {
            "id": feature.get("id"),
            #Qui se non esiste la chiave "place", la forziamo a "Unknown", cioè sconosciuto
            "place": props.get("place", "Unknown"),
            #Convertiamo il magnitudo a float, e lo "forziamo a 0" se è vuoto (None)
            #Discorso analogo vale per i valori sotto
            "magnitude": float(props.get("mag") or 0.0),
            "time": int(props.get("time") or 0),
            "coordinates": geometry.get("coordinates", [0.0, 0.0, 0.0]),
            "tsunami": int(props.get("tsunami") or 0)
        }


    #Questa funzione invia i dati al backend 
    #Ri-utilizza la sessione creata prima (in fetch_and_send) e prende i dati
    #processati dalla funzione sopra e li manda
    def _send_to_backend(self, session: requests.Session, payload: Dict[str, Any]) -> None:

        try:
            #Converto i dati da inviare in JSON e li mando con un timeout di 5 secondi
            #se il backend è bloccato, non aspetto più di 5 secondi per evento
            session.post(self.backend_url, json=payload, timeout=5)
            
        #Loggo l'errore ma non blocco l'intero ciclo di invio degli altri eventi
        except requests.RequestException as e:
            print(f"Errore invio evento {payload['id']}: {e}")

    #Questa funzione mi serve per assicurarmi che sto passando un time_range corretto
    #in modo da tornare l'URL corretto del dizionario
    def get_url_by_range(self, time_range: str) -> Optional[str]:
        return self.usgs_urls.get(time_range)


    #Avvio dello scheduler, ogni 24 ore scarica i dati dell'ultimo giorno
    def run_scheduler(self) -> None:
        schedule.every(24).hours.do(
            #Lambda mi serve per risolvere un problema della libreria schedule.
            #La funzione schedule si aspetta una funzione senza argomenti che può eseguire direttamente
            #Nel nostro caso "fetch_and_send" ha bisogno dell'URL di USGS e del label.
            #Per fare ciò creiamo una funzione lambda che incapsula gli argomenti
            #di fetch_and_send per farla funzionare e dall'esterno "schedule" ha una funzione
            #con zero argomenti come vorrebbe.
            #Se non avessimo gestito la cosa con lamba, lo schedule avrebbe ignorato le 24 ore
            #e avrei passato il valore d'uscita delle funzione fetch_and_send (cioè il count degli eventi)
            #facendolo crashare
            lambda: self.fetch_and_send(self.usgs_urls["day"], "Giornaliero")
        )

        print("Scheduler avviato")
        #Qui teniamo in vita lo schedule, pianificando il fetch ogni 24 ore
        #con una pausa di 1 secondo a ciclo
        while True:
            schedule.run_pending()
            time.sleep(1)



#CONFIGURAZIONE
#Istanziazione della classe 
#Recupero configurazione da variabili d'ambiente 
BACKEND_URL_ENV = os.getenv("BACKEND_URL", "http://backend-go:8080/api/ingest")
sensor_agent = SeismicSensorAgent(BACKEND_URL_ENV)

#DEFINIZIO ENDPOINT API CON FLASK
#Utilizziamo l'istanza 'sensor_agent' all'interno delle rotte
#Qui facciamo in modo che il fetch sia MANUALE, quindi quando vuole l'utente

#Diciamo a Flask di ascoltare l'indirizzo /trigger-fetch e di fare una POST
@app.route('/trigger-fetch', methods=['POST'])
def manual_trigger():
    #Qui leggo la richiesta e la faccio diventare un dizionario nel caso in cui
    #il formato sia corretto
    req_data = request.json or {}
    #Qua cerco il parametro "range" nella richiesta dell'utente
    #se non c'è di default è impostato ad "hour"
    time_range = req_data.get("range", "hour")

    #Traduco la stringa in un URL di USGS
    target_url = sensor_agent.get_url_by_range(time_range)

    #Se non è corretta torno un errore
    if not target_url:
        return jsonify({"Errore": "Inserisci un range valido tra: hour, day, 7days, 30days"}), 400

    print(f"Richiesta di fetch range: {time_range}")

    #Chiamo il metodo definito sopra
    count = sensor_agent.fetch_and_send(target_url, time_range)

    #Torno la risposta in formato JSON 
    return jsonify({
        "Stato": "Completato",
        "Range": time_range,
        "Eventi": count
    }), 200


if __name__ == "__main__":
    print("AVVIO SENSOR AGENT")

    #All'avvio del sistema scarico i dati dell'ultima ora in automatico
    sensor_agent.fetch_and_send(sensor_agent.usgs_urls["hour"], "AVVIO")

    #Qui creo un thread separato per far si che lo scheduler giornaliero non
    #blocchi l'esecuzione dell'app e quindi arrivi all'avvio del server 
    #impostando deamon = True, faccio si che se chiudo il server, anche 
    #questo thread viene ucciso in automatico (e la RAM liberata in automatico)
    t = threading.Thread(target=sensor_agent.run_scheduler, daemon=True)
    t.start()

    app.run(host='0.0.0.0', port=5001)
    print("Sensor Agent attivo sulla porta 5001")