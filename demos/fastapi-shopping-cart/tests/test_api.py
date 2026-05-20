from fastapi.testclient import TestClient

from app.main import app

client = TestClient(app)


def test_create_user_initialize_cart_and_add_item() -> None:
    user_response = client.post("/users", json={"name": "Ada Lovelace", "email": "ada@example.com"})

    assert user_response.status_code == 201
    user_id = user_response.json()["id"]

    cart_response = client.post(f"/users/{user_id}/cart")

    assert cart_response.status_code == 201
    assert cart_response.json() == {"user_id": user_id, "lines": []}

    items_response = client.get("/items")

    assert items_response.status_code == 200
    items = items_response.json()
    assert len(items) == 5

    item_id = items[0]["id"]
    add_response = client.post(f"/users/{user_id}/cart/items/{item_id}", json={"quantity": 2})

    assert add_response.status_code == 200
    body = add_response.json()
    assert body["user_id"] == user_id
    assert body["lines"][0]["item"]["id"] == item_id
    assert body["lines"][0]["quantity"] == 2


def test_filter_items_by_id() -> None:
    item_id = "33333333-3333-4333-8333-333333333333"

    response = client.get(f"/items?id={item_id}")

    assert response.status_code == 200
    assert response.json()[0]["id"] == item_id
    assert len(response.json()) == 1

