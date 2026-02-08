package models

// Earthquake rappresenta il dato sismico ricevuto dai sensori.
// I tag `json` servono per l'API REST, `bson` per MongoDB.
type Earthquake struct {
	ID        string  `json:"id" bson:"_id"`              // ID univoco USGS
	Place     string  `json:"place" bson:"place"`         // Luogo testuale
	Magnitude float64 `json:"magnitude" bson:"magnitude"` // Potenza
	Time      int64   `json:"time" bson:"time"`           // Timestamp Unix
	// Le coordinate GeoJSON sono solitamente [Longitudine, Latitudine, Profondità]
	Coordinates []float64 `json:"coordinates" bson:"coordinates"`
	Tsunami     int       `json:"tsunami" bson:"tsunami"`
	
	IsSimulated bool      `json:"is_simulated" bson:"is_simulated"`
}