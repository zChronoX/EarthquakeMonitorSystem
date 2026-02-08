FROM python:3.10-slim

WORKDIR /app

# Installiamo le dipendenze
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copiamo il codice
COPY stats_service.py .

# Esponiamo la porta
EXPOSE 5000

# Avvio
CMD ["python", "-u", "stats_service.py"]