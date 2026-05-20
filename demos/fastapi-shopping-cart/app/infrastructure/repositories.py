from __future__ import annotations

from decimal import Decimal
from typing import Optional
from uuid import UUID

from app.domain.models import Cart, Item, User
from app.domain.repositories import CartRepository, ItemRepository, UserRepository


class InMemoryUserRepository(UserRepository):
    def __init__(self) -> None:
        self._users: dict[UUID, User] = {}

    def save(self, user: User) -> User:
        self._users[user.id] = user
        return user

    def get(self, user_id: UUID) -> Optional[User]:
        return self._users.get(user_id)


class InMemoryCartRepository(CartRepository):
    def __init__(self) -> None:
        self._carts: dict[UUID, Cart] = {}

    def save(self, cart: Cart) -> Cart:
        self._carts[cart.user_id] = cart
        return cart

    def get_by_user_id(self, user_id: UUID) -> Optional[Cart]:
        return self._carts.get(user_id)


class FakeItemRepository(ItemRepository):
    def __init__(self) -> None:
        self._items = [
            Item(id=UUID("11111111-1111-4111-8111-111111111111"), name="Wireless Mouse", price=Decimal("24.99"), stock=20),
            Item(id=UUID("22222222-2222-4222-8222-222222222222"), name="USB-C Hub", price=Decimal("39.90"), stock=12),
            Item(id=UUID("33333333-3333-4333-8333-333333333333"), name="Mechanical Keyboard", price=Decimal("89.50"), stock=8),
            Item(id=UUID("44444444-4444-4444-8444-444444444444"), name="Laptop Stand", price=Decimal("34.00"), stock=15),
            Item(id=UUID("55555555-5555-4555-8555-555555555555"), name="Noise Cancelling Headphones", price=Decimal("149.99"), stock=6),
        ]

    def list(self, item_id: Optional[UUID] = None) -> list[Item]:
        if item_id is None:
            return list(self._items)
        return [item for item in self._items if item.id == item_id]

    def get(self, item_id: UUID) -> Optional[Item]:
        return next((item for item in self._items if item.id == item_id), None)
