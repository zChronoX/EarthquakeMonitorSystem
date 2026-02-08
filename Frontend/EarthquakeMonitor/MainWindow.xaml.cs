using System;
using System.Collections.Generic; //Serve per le Liste Generiche 
using System.Linq; // Serve per filtrare dati 
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EarthquakeMonitor
{
    public partial class MainWindow : Window
    {
        private DataService _service = new DataService(); // Questo oggetto '_service' incapsula la logica di comunicazione (metodi Get/Post).

        // Variabile di tipo stringa per memorizzare lo stato del filtro temporale.
        private string _currentTimeRange = "hour";

        private List<Earthquake> _allEvents = new List<Earthquake>(); // lista che contiene tutti i terremoti scaricati. Tenuti qui per non doverli riscaricare se l'utente cambia solo un filtro visivo.
        private int _viewLimit = 100; //Limite visivo: quanti terremoti mostrare nella griglia

        public MainWindow()
        {
            InitializeComponent(); // Disegna la finestra (XAML)
            StartDefaultDownload(); // Fa partire subito il download automatico all'apertura
        }

        
        private async void StartDefaultDownload() // Metodo 'async': Gestione avanzata dei task per mantenere l'UI reattiva. Permette all'interfaccia di non bloccarsi mentre si scaricano i dati.
        {
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait; // Cambia il cursore in "Clessidra" per far capire all'utente che sta lavorando.
            TxtSummary.Text = "Inizializzazione: Pulizia DB e Download ultima ora"; // Scrive un messaggio nell'header per dare feedback immediato.

            try
            {
                // Chiamate asincrone ai metodi del DataService.
                await _service.CleanupAsync(1); // Ordina al Backend Go di cancellare i dati vecchi(> 1 ora). Serve per partire con una situazione pulita.

                
                await _service.TriggerFetchAsync("hour"); // Ordina al Backend Go di scaricare nuovi dati dall'USGS (ultima ora). Go scarica, salva su Mongo, e ci risponde "OK".

               
                LoadData(); 
            }
            catch (Exception ex) { MessageBox.Show("Errore avvio: " + ex.Message); } 
            finally { System.Windows.Input.Mouse.OverrideCursor = null; }// Il blocco 'finally' viene eseguito SEMPRE, sia se va tutto bene, sia se c'è errore.
                                                                         // Ottimo per pulire le risorse (ripristinare il cursore).
        }

        private void UpdateGridVisibility(List<Earthquake> eventsToShow, string zona = "") // Decide se mostrare la griglia (se ci sono dati) o la scritta "Nessun dato" (se vuota).
        {
            GridEarthquakes.ItemsSource = eventsToShow; // Collega la lista filtrata alla griglia XAML.

            if (eventsToShow.Count == 0)
            {
                // Nasconde griglia, mostra avviso "Nessun dato"
                GridEarthquakes.Visibility = Visibility.Collapsed;
                TxtNoData.Visibility = Visibility.Visible;
                // Se la zona è vuota ? scrive "Nessun dato" : altrimenti scrive "Nessun dato per 'zona'".
                TxtNoData.Text = string.IsNullOrEmpty(zona) ? "Nessun dato disponibile." : $"Nessun terremoto trovato per: '{zona}'";
            }
            else
            {
                // Mostra griglia, nasconde avviso
                GridEarthquakes.Visibility = Visibility.Visible;
                TxtNoData.Visibility = Visibility.Collapsed;
            }
        }

        // Applica i filtri locali (senza richiamare il server).
        // Filtra per numero (es. primi 100) e aggiorna la vista.
        private void ApplyVisualLimit()
        {
            // se la lista è null, esce
            if (_allEvents == null) return;

            // Recupera cosa ha scritto l'utente 
            string text = TxtPlaceSearch.Text.Trim();

            // Creazione query LINQ
            IEnumerable<Earthquake> query = _allEvents;

            // Applicazione filtro intelligente 
            if (!string.IsNullOrEmpty(text))
            {
                query = query.Where(e =>
                {
                    
                    string completeZone = e.Place;

                    // Indivisuazione del separatore " of " (tipico formato USGS)
                    int indexOf = completeZone.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);

                    if (indexOf >= 0)
                    {
                        //Eliminazione del contenuto precedente ad 'of'
                        // +4 serve a saltare i caratteri ' ', 'o', 'f', ' '
                        completeZone = completeZone.Substring(indexOf + 4);
                    }

                    //Ricerca del testo nella parte filtrata
                    // IndexOf >= 0 è il modo più veloce per dire "Contiene" ignorando maiuscole/minuscole
                    return completeZone.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;

                });
            }

            //Si applica il limite numerico (se esiste) 
            if (_viewLimit > 0)
            {
                query = query.Take(_viewLimit);
            }

            // Lista finale 
            List<Earthquake> filteredList = query.ToList();

            // Passaggio dati alla grafica 
            UpdateGridVisibility(filteredList, text);
        }

        private async void LoadData()
        {
            TxtSummary.Text = "Aggiornamento statistiche";

            try
            {
                // Legge la scrittura nella casella di ricerca.
                // .Trim() rimuove gli spazi bianchi all'inizio e alla fine
                string zona = TxtPlaceSearch.Text.Trim();

                // Chiamata a Go. Chiede al servizio di recuperare i dati dal Database MongoDB.
                // Parametri: Magnitudo min 0, Luogo (zona), Limite 20000 (tutti).
                _allEvents = await _service.GetEventsAsync(minMag: 0, place: zona, limit: 20000);

                // Applica il limite visivo (es. 100 righe)
                ApplyVisualLimit();

                // Chiamata a Python. Chiede le statistiche avanzate (Totale, Max, Rischio).
                // Passa la zona e l'intervallo di tempo corrente.
                // Recupero statistiche, oggetto complesso StatsData
                var stats = await _service.GetStatsAsync(place: zona, timeRange: _currentTimeRange);

                if (stats != null)
                {
                    // Aggiorna le etichette nell'header della finestra.
                    LblTotal.Text = $"Eventi Totali: {stats.TotalEvents}";
                    LblMax.Text = $"Max Mag: {stats.MaxMagnitude:0.0}"; // Formattazione 1 decimale
                    TxtSummary.Text = stats.SummaryText;

                    //Gestione dei colori
                    // Accesso a proprietà annidate (stats -> RiskStatus).
                    if (stats.RiskStatus != null)
                    {
                        bool colorApplied = false;

                        
                        if (!string.IsNullOrEmpty(stats.RiskStatus.ColorCode))
                        {
                            try
                            {
                                // Utilizzo di una classe di sistema (System.Windows.Media) per convertire stringa HEX in Oggetto Brush.
                                var converter = new System.Windows.Media.BrushConverter();
                                // Conversione del risultato generico (object) in (Brush).
                                TxtSummary.Foreground = (Brush)converter.ConvertFromString(stats.RiskStatus.ColorCode);
                                colorApplied = true;
                            }
                            catch { 
                            }
                        }

                        // Se il colore non è stato applicato, si usano i Livelli
                        if (!colorApplied)
                        {
                            // Alternativa elegante a una lunga catena di if-else if.
                            // Controlla il valore di 'stats.RiskStatus.Level'.
                            switch (stats.RiskStatus.Level)
                            {
                                case "CRITICO":
                                    TxtSummary.Foreground = Brushes.Crimson;
                                    break;
                                case "ATTENZIONE":
                                    TxtSummary.Foreground = Brushes.DarkOrange;
                                    break;
                                case "PRE-ALLERTA":
                                    TxtSummary.Foreground = Brushes.DarkGoldenrod;
                                    break;
                                case "NORMALE":
                                    TxtSummary.Foreground = Brushes.ForestGreen;
                                    break;
                                default:
                                    TxtSummary.Foreground = Brushes.Black;
                                    break;
                            }
                        }
                    }
                    else
                    {
                        TxtSummary.Foreground = Brushes.Black;
                    }
                }
            }
            catch (Exception ex)
            {
                TxtSummary.Text = "Errore Connessione Backend.";
            }
        }


        // Questi metodi vengono invocati quando l'utente interagisce con l'UI.
        // Bottone cambia numero righe
        private void BtnApplyLimit_Click(object sender, RoutedEventArgs e)
        {
            // 'sender' è un object generico (polimorfismo). 
            // Trattato come 'ComboBoxItem' per accedere alle sue proprietà specifiche.
            var item = CmbLimit.SelectedItem as ComboBoxItem; // Capisce quale opzione è stata scelta (50, 100, Tutti) dal "Tag" dello XAML
            if (item != null && item.Tag != null && int.TryParse(item.Tag.ToString(), out int newLimit)) // Parsing sicuro da stringa a intero
            {
                _viewLimit = newLimit;
                ApplyVisualLimit(); // Ridisegna solo la griglia (niente server)
            }
        }
        // Bottone aggiornamento "Ultima Ora"
        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            
            _currentTimeRange = "hour"; // Imposta stato
            await _service.TriggerFetchAsync("hour"); // Scarica nuovi dati da USGS
            LoadData(); // Ricarica lista e statistiche
        
        }

        // Bottone temporale 24 ore 
        private async void BtnFetch24Hours_Click(object sender, RoutedEventArgs e)
        {
           
            // Conversione 'sender' (il bottone cliccato) in 'Button' 
            // per poter accedere alla proprietà .IsEnabled.
            var btn = (Button)sender;
            btn.IsEnabled = false;  // Disabilita click
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                _currentTimeRange = "day"; // Imposta stato a "giorno"
                MessageBox.Show("Download dati ultime 24 ore avviato");
                await _service.TriggerFetchAsync("day"); // Effettua download 
                LoadData(); // Aggiorna UI 
                MessageBox.Show("Download completato!");
            }
            catch (Exception ex) { MessageBox.Show("Errore: " + ex.Message); }
            finally { System.Windows.Input.Mouse.OverrideCursor = null; btn.IsEnabled = true; } // Riabilita tutto
        
        }

        // Bottone temporale 7 giorni
        private async void BtnFetch7Days_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            btn.IsEnabled = false;
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                _currentTimeRange = "7days";
                MessageBox.Show("Download ultimi 7 giorni avviato");
                await _service.TriggerFetchAsync("7days");
                LoadData();
                MessageBox.Show("Download Completato!");
            }
            catch (Exception ex) { MessageBox.Show("Errore: " + ex.Message); }
            finally { System.Windows.Input.Mouse.OverrideCursor = null; btn.IsEnabled = true; }
        }

        // Bottone temporale 30 giorni 
        private async void BtnFetch30Days_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            btn.IsEnabled = false;
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                _currentTimeRange = "30days";
                MessageBox.Show("Avvio download ultimi 30 giorni");
                await _service.TriggerFetchAsync("30days");
                LoadData();
                MessageBox.Show("Download completato!");
            }
            catch (Exception ex) { MessageBox.Show("Errore: " + ex.Message); }
            finally { System.Windows.Input.Mouse.OverrideCursor = null; btn.IsEnabled = true; }
        }

        //Bottone simulazione 
        private async void BtnSimulate_Click(object sender, RoutedEventArgs e)
        {
            // Chiama l'endpoint Go che crea un terremoto finto casuale
            await _service.SimulateEventAsync();
            LoadData(); // Ricarica per vederlo apparire in lista
            MessageBox.Show("⚠️ Terremoto simulato generato!");
        }

        // Bottone pulizia DB
        private async void BtnCleanup_Click(object sender, RoutedEventArgs e)
        {
            string msg = "Sei sicuro?\n\n1. Verranno cancellati i dati reali più vecchi di 1 ORA.\n2. Verranno cancellati TUTTI i terremoti simulati.";
            if (MessageBox.Show(msg, "Conferma Pulizia Totale", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _currentTimeRange = "hour"; // Resetta vista a 1 ora
                await _service.CleanupAsync(1); // Chiama Go per cancellare
                LoadData(); // Aggiorna la lista
                MessageBox.Show("Pulizia DB completata!");
            }
        }

        // Bottone Export CSV
        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Chiede a Go l'URL per scaricare il CSV
                string url = _service.GetCsvLink();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); // Utilizzo di classi di sistema (System.Diagnostics) per interagire con l'OS. Apre il browser predefinito di Windows su quell'URL
            }
            catch { MessageBox.Show("Errore apertura browser."); }
        }

        //Bottone ricerca 
        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {

            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try { LoadData(); } // La logica di filtro (place: zona) è dentro LoadData().
            catch (Exception ex) { MessageBox.Show("Errore ricerca: " + ex.Message); }
            finally { System.Windows.Input.Mouse.OverrideCursor = null; }
        }

        //Bottone reset ricerca 
        private void BtnResetSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtPlaceSearch.Text = ""; // Pulisce casella
            LoadData(); // Ricarica tutto
        }

        // Bottone grafici 
        private void BtnGraphs_Click(object sender, RoutedEventArgs e)
        {
            if (_allEvents != null && _allEvents.Count > 0) // Controlla se ci sono dati da mostrare
            {
                GraphsWindow win = new GraphsWindow(_allEvents); // Crea la nuova finestra (GraphsWindow) passando la lista dei terremoti
                win.Show(); // La apre sopra la finestra attuale
            }
            else
            {
                MessageBox.Show("Scarica prima i dati per vedere i grafici!");
            }
        }
    }
}