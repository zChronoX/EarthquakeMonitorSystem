#Usiamo l'ultima versione di Go
FROM golang:1.25-alpine

WORKDIR /app

#Copia i file delle dipendenze
COPY go.mod go.sum ./
RUN go mod download

#Copia il codice sorgente
COPY . .

#Compila
RUN go build -o main .

#Espone la porta
EXPOSE 8080

#Esegue il main
CMD ["./main"]