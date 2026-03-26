## Daily Commands

| Task | Command |
|------|---------|
| Start all services | `podman compose up -d` |
| Stop all services | `podman compose down` |
| Restart one service | `podman compose restart jarvis-api` |
| Rebuild + restart one | `podman compose build jarvis-api && podman compose up -d jarvis-api` |
| View all logs | `podman compose logs -f` |
| Andrew logs only | `podman compose logs -f andrew-agent` |
| Jarvis API logs only | `podman compose logs -f jarvis-api` |
| Vault logs | `podman compose logs -f infisical` |
| Service health | `podman compose ps` |
| Shell into Andrew | `podman compose exec andrew-agent bash` |
| Shell into Jarvis API | `podman compose exec jarvis-api bash` |
| Postgres shell | `podman compose exec postgres psql -U mediahostai mediahostai` |
| List all containers | `podman ps -a` |
| Force server scan | `curl -X POST http://localhost:5001/api/andrew/discover/all` |
| Get production certs | `bash scripts/get-certs.sh` |
| Renew certs manually | `podman compose run --rm certbot renew` |
