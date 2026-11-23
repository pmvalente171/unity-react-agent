FROM python:3.12-slim

WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY server.py .
COPY core/ core/
COPY agent/ agent/
COPY llm/ llm/
COPY config/ config/

ENV PORT=5000
EXPOSE 5000
CMD ["sh", "-c", "gunicorn --bind 0.0.0.0:${PORT} --workers 2 --timeout 120 'server:create_default_app()'"]
