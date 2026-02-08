using System;
using System.Collections.Generic; //Serve per le Liste Generiche 
using System.Net.Http; // Serve per fare richieste web (GET, POST)
using System.Text;
using System.Text.Json; // Serve per tradurre il testo JSON in oggetti C#
using System.Threading.Tasks; //Serve per la gestione dei Task e operazioni asincrone

namespace EarthquakeMonitor
{
    public class DataService // Questa classe funge da "ponte" (incapsulamento della logica di comunicazione).
                             // Il resto del programma non deve sapere come funzionano le chiamate HTTP, deve solo chiamare i metodi di questa classe.
    {
        // _client è come il browser web, serve a inviare le richieste HTTP.
        private readonly HttpClient _client;
        private const string GO_API = "http://localhost:8080/api"; // GO_API (8080): Si occupa dei dati grezzi, download da USGS, pulizia DB.
        private const string PY_API = "http://localhost:5000/api"; // PY_API (5000): Si occupa dell'intelligenza, statistiche e analisi rischio.

        public DataService() // Inizializza le risorse necessarie (in questo caso, l'HttpClient).
        {
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromMinutes(5); // Si imposta un timeout lungo (5 minuti).
        }

        // "async" / "await": Permettono di non bloccare l'interfaccia utente durante l'attesa della risposta dal server.
        // Parametri opzionali (minMag = 0) permettono di chiamare il metodo omettendo alcuni argomenti.
        // Scarica la lista degli eventi dal database (tramite Go). 
        public async Task<List<Earthquake>> GetEventsAsync(double minMag = 0, string place = "", int limit = 0)
        {
            try
            {
                // Costruisce l'URL dinamico tramite interpolazione di stringhe ($)
                var url = $"{GO_API}/events?min_mag={minMag}&place={place}";

                
                if (limit > 0)
                {
                    url += $"&limit={limit}";
                }

                // Esegue la chiamata GET asincrona. 'await' sospende l'esecuzione di questo metodo (ma non di tutta l'app) finché non arrivano i dati.

                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode(); // Lancia un'eccezione se il codice HTTP è di errore (es. 404).

                // Legge il corpo della risposta come stringa (è un lungo testo JSON).
                var json = await response.Content.ReadAsStringAsync();

                // Configurazione per ignorare le maiuscole/minuscole nel JSON
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                // Trasforma la stringa JSON in una Lista di oggetti Earthquake veri e propri.
                return JsonSerializer.Deserialize<List<Earthquake>>(json, options);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore: {ex.Message}");
                return new List<Earthquake>();
            }
        }

        public async Task<StatsData> GetStatsAsync(string place = "", string timeRange = "day")
        {
            try
            {
                var url = $"{PY_API}/stats?place={place}&range={timeRange}"; // Chiama l'endpoint Python che calcola totali e rischio.
                var json = await _client.GetStringAsync(url); // Scarica direttamente la stringa JSON.
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true }; // Trasforma il JSON nell'oggetto StatsData 
                return JsonSerializer.Deserialize<StatsData>(json, options); // Restituisce un singolo oggetto StatsData (non una lista).
            }
            catch { 
                return null; 
            }
        }

        // Metodo void asincrono (Task senza <T> è come void) per inviare comandi (POST).
        public async Task TriggerFetchAsync(string range)
        {
            try {
                // Crea un pacchetto JSON
                var payload = new { range = range };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                // Usa POST invece di GET perché si sta modificando lo stato del server (chiedendo un'azione).
                await _client.PostAsync($"{GO_API}/fetch-now", content);
            } 
            catch {
            }
        }
        // Ordina a Go di creare un terremoto finto per testare l'allarme.
        public async Task SimulateEventAsync()
        {
            try
            {
                await _client.PostAsync($"{GO_API}/simulate", null); // Chiamata POST senza corpo (null) per attivare la simulazione.
            }
            catch
            {

            }
        }
        // Ordina a Go di cancellare i dati vecchi dal Database.
        public async Task CleanupAsync(int hours)
        {
            try
            {
                await _client.DeleteAsync($"{GO_API}/cleanup?hours={hours}"); // Chiamata DELETE: Specifica semantica per la cancellazione dati.
            }
            catch
            {

            }
        }
        // Restituisce solo l'indirizzo internet per scaricare il CSV, che poi viene passato al Browser.
        public string GetCsvLink() { 
            return $"{GO_API}/export";
        }

    }
}