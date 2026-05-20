# FastAPI Shopping Cart Sandbox

Small FastAPI sandbox that demonstrates account creation, cart initialization,
item lookup, and adding catalog items to a customer cart.

## Run

```bash
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
uvicorn app.main:app --reload
```

Open `http://127.0.0.1:8000/docs` for the generated OpenAPI UI.

## Endpoints

- `POST /users` creates a user account.
- `POST /users/{user_id}/cart` creates or initializes the user's shopping cart.
- `GET /items` returns available items and can filter by `id`.
- `POST /users/{user_id}/cart/items/{item_id}` adds an item to the user's cart.

