FROM python:3.10-slim

WORKDIR /app

# Installa le dipendenze
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copia lo script
COPY sensor_agent.py .

# Avvia lo script con l'opzione -u (unbuffered) per vedere subito i print nei log
CMD ["python", "-u", "sensor_agent.py"]