using System;
using System.Collections.Generic; //Serve per le Liste Generiche 
using System.Linq; // Serve per filtrare dati 
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes; // Serve per usare Ellipse (Cerchi) e Rectangle (Barre)

namespace EarthquakeMonitor
{
    // È una classe "Partial" perché metà del codice è qui, l'altra metà è gestita automaticamente dallo XAML.
    // salva la lista dei terremoti passata dalla MainWindow
    public partial class GraphsWindow : Window
    {
        private List<Earthquake> _events;

        public GraphsWindow(List<Earthquake> events) // Accetta in ingresso la lista dei dati passata dalla MainWindow.
        {
            InitializeComponent();
            _events = events ?? new List<Earthquake>(); // Se 'events' è null, crea una nuova lista vuota per evitare crash (NullReferenceException).


            // Si deve aspettare l'evento "Loaded" (finestra caricata e visibile) perché la finestra non ha ancora una dimensione.
            this.Loaded += (s, e) => DrawMap(); // Disegna la mappa appena appare la finestra
            // Se l'utente allarga la finestra, il grafico deve ridisegnarsi per adattarsi.
            MagHistogramCanvas.SizeChanged += (s, e) => DrawMagnitudeHistogram();
            this.Loaded += (s, e) => DrawLocationsChart(); // Carica la classifica dei luoghi
        }

        // Metodo helper che incapsula la logica dei colori. Restituisce un oggetto SolidColorBrush.
        // determinare il colore in base al valore double della magnitudo.
        private SolidColorBrush GetMagnitudeColor(double mag)
        {
            if (mag < 3.0) return Brushes.SpringGreen;
            if (mag < 4.5) return Brushes.Gold;
            if (mag < 6.0) return Brushes.DarkOrange;
            return Brushes.Crimson;
        }

        // Metodo per disegnare la mappa (Puntini rossi)
        private void DrawMap()
        {
            MapCanvas.Children.Clear(); // Metodo della classe Canvas per pulire la grafica precedente.
            double w = MapCanvas.ActualWidth;
            double h = MapCanvas.ActualHeight;

            if (w == 0 || h == 0 || _events.Count == 0) return; // Se la finestra è troppo piccola o non ci sono dati, non fa nulla.

            foreach (var eq in _events)
            {
                // Controllo sicurezza: le coordinate devono esistere e avere almeno 2 numeri (Lon, Lat)
                if (eq.Coordinates == null || eq.Coordinates.Count < 2) continue; 

                double lon = eq.Coordinates[0]; // Longitudine (-180 a +180) -> Asse X
                double lat = eq.Coordinates[1]; // Latitudine (-90 a +90) -> Asse Y

                // Si trasformano le coordinate geografiche in Pixel dello schermo.
                // X: Sposta tutto di +180 per togliere i negativi, poi proporziona la larghezza (w).
                double x = (lon + 180) * (w / 360.0);
                // Y: Le coordinate vanno dal basso all'alto, ma i computer disegnano dall'alto al basso.
                // Ecco perché si fa (90 - lat).
                double y = (90 - lat) * (h / 180.0);

                Ellipse dot = new Ellipse // Sintassi per creare un oggetto (Ellipse) e settare subito le sue proprietà
                {
                    Width = 8,
                    Height = 8,
                    Fill = Brushes.Red,
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.5,
                    // ToolTip: Se si passa il mouse sopra il puntino, esce la finestrella con i dati
                    ToolTip = $"{eq.Place}\nMag: {eq.Magnitude}\nData: {eq.FormattedDate}"
                };

                // Metodi statici della classe Canvas per posizionare l'elemento (Positioning)
                Canvas.SetLeft(dot, x - 4);
                Canvas.SetTop(dot, y - 4);

                MapCanvas.Children.Add(dot); // Aggiunge il puntino alla tela (Canvas)
            }
        }

        // Metodo per disegnare l'Istogramma
        private void DrawMagnitudeHistogram()
        {
            MagHistogramCanvas.Children.Clear();

            double w = MagHistogramCanvas.ActualWidth;
            double h = MagHistogramCanvas.ActualHeight;

            if (w == 0 || h == 0) return;

            // Definisce l'asse X: Magnitudo da 0 a 10
            int minX = 0;
            int maxX = 10;
            int totalBars = maxX - minX + 1; // Totale 11 barre

            // trova la colonna più alta
            int maxCount = 0;
            if (_events.Count > 0)
            {
                var groups = _events.GroupBy(e => (int)e.Magnitude);  // .GroupBy: Raggruppa i terremoti che hanno la stessa magnitudo intera.
                
                if (groups.Any()) maxCount = groups.Max(g => g.Count()); // .Max: Trova il gruppo più numeroso (serve per scalare l'altezza delle barre).
            }
            if (maxCount == 0) maxCount = 1; // Evita divisione per zero

            // Calcola larghezza barre e spazi
            double barWidth = (w / totalBars) * 0.7;
            double spacing = (w / totalBars) * 0.3;

            // Ciclo da 0 a 10 per disegnare le barre
            for (int i = minX; i <= maxX; i++)
            {
                // Conta quanti eventi soddisfano la condizione (Magnitude == i).
                int count = _events.Count(e => (int)e.Magnitude == i);

                // Calcola altezza in pixel
                double barHeight = 0;
                if (count > 0)
                {
                    barHeight = (count / (double)maxCount) * (h - 60); // Bisogna castare a (double) per avere la divisione con la virgola
                    if (barHeight < 2) barHeight = 2;
                }

                Rectangle bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    // Se ci sono eventi usa il colore dinamico, altrimenti trasparente
                    Fill = count > 0 ? GetMagnitudeColor(i) : Brushes.Transparent,
                    Stroke = count > 0 ? Brushes.Black : Brushes.Transparent,
                    StrokeThickness = 0.5,
                    ToolTip = $"Magnitudo {i}: {count} eventi"
                };
                // Calcola posizione X e Y (il computer disegna dall'alto, quindi Y = AltezzaTotale - AltezzaBarra)
                double x = i * (barWidth + spacing) + spacing;
                double y = h - barHeight - 30;

                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, y);
                MagHistogramCanvas.Children.Add(bar);

                TextBlock label = new TextBlock // Creazione etichetta testo (codice UI via C# invece che XAML).
                {
                    Text = i.ToString(),
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    Foreground = Brushes.Black
                };
                // Centratura del testo (calcoli geometrici).
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double textWidth = label.DesiredSize.Width;
                Canvas.SetLeft(label, x + (barWidth / 2) - (textWidth / 2));
                Canvas.SetTop(label, h - 25);
                MagHistogramCanvas.Children.Add(label);

                // Se ci sono dati, scrive il conteggio sopra la barra
                if (count > 0)
                {
                    TextBlock countLabel = new TextBlock
                    {
                        Text = count.ToString(),
                        FontSize = 12,
                        Foreground = Brushes.Black,
                        FontWeight = FontWeights.SemiBold
                    };

                    countLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double countWidth = countLabel.DesiredSize.Width;
                    Canvas.SetLeft(countLabel, x + (barWidth / 2) - (countWidth / 2));
                    Canvas.SetTop(countLabel, y - 20);
                    MagHistogramCanvas.Children.Add(countLabel);
                }
            }
        }

        private void DrawLocationsChart()
        {
            LocationsPanel.Children.Clear();
            if (_events.Count == 0) return;

            // Questa è una "Pipeline" di dati. Ogni operazione trasforma i dati per il passaggio successivo.
            var locationGroups = _events  
                .Select(e => e.Place.Contains(",") ? e.Place.Split(',').Last().Trim() : e.Place) // Pulisce i nomi (prende la parte dopo la virgola)
                .GroupBy(loc => loc) //Raggruppa per nazione
                .Select(g => new {
                    Location = g.Key,
                    Count = g.Count() //Conta quanti sono per ogni nazione
                })
                .OrderByDescending(x => x.Count) //Ordina dal più grande al più piccolo
                .Take(15) //Prende i primi 15
                .ToList(); //Esegue la query e restituisce una List generica concreta.

            if (locationGroups.Count == 0) return;
            int maxCount = locationGroups.Max(x => x.Count); //Serve per calcolare la percentuale della barra

            // Contatore per la classifica
            int rank = 0;

            foreach (var item in locationGroups) // 'item' qui è un tipo anonimo creato dalla LINQ sopra
            {
                rank++;

                // Creazione dinamica della griglia (UI).
                Grid row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Nome nazione
                TextBlock txt = new TextBlock
                {
                    Text = $"{rank}. {item.Location}",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = rank <= 3 ? FontWeights.Bold : FontWeights.Normal, // I primi 3 in grassetto
                    FontSize = 14,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                // logica colori podio 
                SolidColorBrush barColor;
                if (rank == 1) barColor = Brushes.Crimson;     
                else if (rank == 2) barColor = Brushes.DarkOrange;  
                else if (rank == 3) barColor = Brushes.Gold;       
                else barColor = Brushes.ForestGreen; 

                //Barra di progresso
                ProgressBar bar = new ProgressBar
                {
                    Value = item.Count,
                    Maximum = maxCount,
                    Height = 25,
                    Foreground = barColor, // Applica il colore del podio
                    BorderBrush = Brushes.Gray,
                    ToolTip = $"{item.Count} eventi in {item.Location}"
                };

                //Posiziona nella griglia 
                Grid.SetColumn(txt, 0);
                Grid.SetColumn(bar, 1);
                row.Children.Add(txt);
                row.Children.Add(bar);

                //Aggiunge la riga completa nella lista 
                LocationsPanel.Children.Add(row);
            }
        }
    }
}