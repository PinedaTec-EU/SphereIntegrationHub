from __future__ import annotations

from app.application.use_cases import AddItemToCart, CreateUserAccount, InitializeCart, ListAvailableItems
from app.infrastructure.repositories import FakeItemRepository, InMemoryCartRepository, InMemoryUserRepository

users = InMemoryUserRepository()
carts = InMemoryCartRepository()
items = FakeItemRepository()


def get_create_user_account() -> CreateUserAccount:
    return CreateUserAccount(users)


def get_initialize_cart() -> InitializeCart:
    return InitializeCart(users, carts)


def get_list_available_items() -> ListAvailableItems:
    return ListAvailableItems(items)


def get_add_item_to_cart() -> AddItemToCart:
    return AddItemToCart(users, carts, items)
