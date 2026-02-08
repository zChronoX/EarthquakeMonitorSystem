using System;
using System.Globalization; // Per gestire le culture (es. virgola vs punto nei decimali), richiesto dall'interfaccia.
using System.Windows.Data; //Namespace che contiene l'interfaccia IValueConverter.
using System.Windows.Media; // Namespace per i colori (Brushes).

namespace EarthquakeMonitor
{
    public class MagnitudeToColorConverter : IValueConverter // La classe implementa (:) l'interfaccia "IValueConverter".
                                                             // Ovvero un "contratto" che obbliga questa classe a contenere
                                                             // esattamente due metodi: Convert e ConvertBack. Senza di essi, il codice non compilerebbe.
    {
        // Il metodo accetta 'object value'. Poiché la Magnitudo è un 'double' (Value Type),
        // qui arriva "inscatolata" dentro un object (Reference Type).
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Invece di fare un cast brutale ((double)value) che potrebbe crashare se value è null,
            // si usa 'is'. Se value è un double, lo si "sbusta" e lo si mette nella variabile 'mag'.
            if (value is double mag)
            {
                // Logica sequenziale per determinare il colore.
                // Appena trova una condizione vera, esce con 'return'.
                if (mag >= 6.0) return Brushes.DarkRed;       // Catastrofico
                if (mag >= 5.0) return Brushes.OrangeRed;     // Forte
                if (mag >= 4.0) return Brushes.Orange;        // Medio
                if (mag >= 2.0) return Brushes.LightYellow;   // Leggero
            }
            return Brushes.Transparent; // Valore di default se l'input non è un numero o è molto basso.
        }

        // Questo metodo serve se si volesse modificare il colore per cambiare il numero (impossibile qui).
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}