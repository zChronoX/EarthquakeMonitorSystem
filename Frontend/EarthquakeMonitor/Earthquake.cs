using System.Collections.Generic; //Serve per le Liste Generiche 
using System.Text.Json.Serialization; // libreria che traduce il JSON in C#

namespace EarthquakeMonitor
{
    // Modello Dati Terremoto. I dati arrivano dal Backend Go (che li ha presi da USGS).
    public class Earthquake
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } // Quando dal server arriva un campo chiamato 'id', lo mette dentro la variabile 'Id'.

        [JsonPropertyName("place")]
        public string Place { get; set; }

        [JsonPropertyName("magnitude")]
        public double Magnitude { get; set; }

        [JsonPropertyName("time")] // Il server manda il tempo in formato "Unix Timestamp" (millisecondi dal 1970). 
        public long Time { get; set; }

        [JsonPropertyName("coordinates")]
        public List<double> Coordinates { get; set; } // Le coordinate arrivano come array: [Longitudine, Latitudine, Profondità]

        [JsonPropertyName("is_simulated")]
        public bool IsSimulated { get; set; } // Serve per colorare o evidenziare diversamente i terremoti finti generati dal tasto "Simula"

        
        [JsonPropertyName("tsunami")] // Il server manda un numero intero (0 = No Tsunami, 1+ = Allerta Tsunami)
        public int Tsunami { get; set; }


        // proprietà calcolata dal C# per aiutare la grafica. Estrae la Profondità:
        //    Il server la mette dentro la lista Coordinates al terzo posto (indice 2).
        //    Il controllo (Coordinates != null) serve a non far crashare l'app se la lista è vuota.
        public double Depth => (Coordinates != null && Coordinates.Count > 2) ? Coordinates[2] : 0;

        //Tsunami Leggibile: Nella griglia si vede "Sì" o "No"
        public string TsunamiStatus => (Tsunami > 0) ? "SI" : "No";

        //    Trasforma quel numero (Time) in una data vera.
        //    Usa la funzione DateTimeOffset per convertire i millisecondi.
        public string FormattedDate => System.DateTimeOffset.FromUnixTimeMilliseconds(Time).LocalDateTime.ToString("dd/MM HH:mm");
    }

    // Questi dati arrivano dal servizio Python (Analytics).
    // Servono per riempire l'header in alto nell'app (Totale eventi, Max Mag, Rischio).
    public class StatsData
    {
        [JsonPropertyName("total_events")]
        public int TotalEvents { get; set; }

        [JsonPropertyName("max_magnitude")]
        public double MaxMagnitude { get; set; }

        [JsonPropertyName("summary_text")]
        public string SummaryText { get; set; }

        [JsonPropertyName("risk_status")]
        public RiskInfo RiskStatus { get; set; }
    }

    // Sotto-classe usata dentro StatsData per gestire colori e livelli di allerta.
    public class RiskInfo
    {
        [JsonPropertyName("level")]
        public string Level { get; set; }
        [JsonPropertyName("color_code")]
        public string ColorCode { get; set; }
    }

    // Calcola la distanza dal terremoto più vicino.
    public class SafetyResult
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } // "SAFE" o "DANGER"
        [JsonPropertyName("message")]
        public string Message { get; set; }
        [JsonPropertyName("nearest_earthquake_km")]
        public double DistanceKm { get; set; }
    }
}