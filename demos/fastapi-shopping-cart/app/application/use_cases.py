from __future__ import annotations

from typing import Optional
from uuid import UUID, uuid4

from app.domain.models import Cart, Item, User
from app.domain.repositories import CartRepository, ItemRepository, UserRepository


class UserNotFoundError(Exception):
    pass


class ItemNotFoundError(Exception):
    pass


class CreateUserAccount:
    def __init__(self, users: UserRepository) -> None:
        self._users = users

    def execute(self, name: str, email: str) -> User:
        return self._users.save(User(id=uuid4(), name=name, email=email))


class InitializeCart:
    def __init__(self, users: UserRepository, carts: CartRepository) -> None:
        self._users = users
        self._carts = carts

    def execute(self, user_id: UUID) -> Cart:
        if self._users.get(user_id) is None:
            raise UserNotFoundError("User account was not found.")

        existing = self._carts.get_by_user_id(user_id)
        if existing is not None:
            return existing

        return self._carts.save(Cart(user_id=user_id))


class ListAvailableItems:
    def __init__(self, items: ItemRepository) -> None:
        self._items = items

    def execute(self, item_id: Optional[UUID] = None) -> list[Item]:
        return self._items.list(item_id)


class AddItemToCart:
    def __init__(
        self,
        users: UserRepository,
        carts: CartRepository,
        items: ItemRepository,
    ) -> None:
        self._users = users
        self._carts = carts
        self._items = items

    def execute(self, user_id: UUID, item_id: UUID, quantity: int = 1) -> Cart:
        if self._users.get(user_id) is None:
            raise UserNotFoundError("User account was not found.")

        item = self._items.get(item_id)
        if item is None:
            raise ItemNotFoundError("Item was not found.")

        cart = self._carts.get_by_user_id(user_id) or Cart(user_id=user_id)
        cart.add_item(item, quantity)
        return self._carts.save(cart)
