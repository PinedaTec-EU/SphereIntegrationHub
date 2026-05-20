from __future__ import annotations

from dataclasses import dataclass, field
from decimal import Decimal
from uuid import UUID


@dataclass(frozen=True)
class User:
    id: UUID
    name: str
    email: str


@dataclass(frozen=True)
class Item:
    id: UUID
    name: str
    price: Decimal
    stock: int


@dataclass
class CartLine:
    item: Item
    quantity: int = 1


@dataclass
class Cart:
    user_id: UUID
    lines: dict[UUID, CartLine] = field(default_factory=dict)

    def add_item(self, item: Item, quantity: int = 1) -> None:
        if quantity <= 0:
            raise ValueError("Quantity must be greater than zero.")
        if quantity > item.stock:
            raise ValueError("Requested quantity exceeds available stock.")

        existing = self.lines.get(item.id)
        if existing is None:
            self.lines[item.id] = CartLine(item=item, quantity=quantity)
            return

        new_quantity = existing.quantity + quantity
        if new_quantity > item.stock:
            raise ValueError("Requested quantity exceeds available stock.")
        existing.quantity = new_quantity
