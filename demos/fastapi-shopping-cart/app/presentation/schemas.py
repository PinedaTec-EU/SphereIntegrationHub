from __future__ import annotations

from decimal import Decimal
from uuid import UUID

from pydantic import BaseModel, EmailStr, Field

from app.domain.models import Cart, Item, User


class CreateUserRequest(BaseModel):
    name: str = Field(min_length=1, max_length=120)
    email: EmailStr


class UserResponse(BaseModel):
    id: UUID
    name: str
    email: str

    @classmethod
    def from_domain(cls, user: User) -> "UserResponse":
        return cls(id=user.id, name=user.name, email=user.email)


class ItemResponse(BaseModel):
    id: UUID
    name: str
    price: Decimal
    stock: int

    @classmethod
    def from_domain(cls, item: Item) -> "ItemResponse":
        return cls(id=item.id, name=item.name, price=item.price, stock=item.stock)


class AddItemRequest(BaseModel):
    quantity: int = Field(default=1, ge=1)


class CartLineResponse(BaseModel):
    item: ItemResponse
    quantity: int


class CartResponse(BaseModel):
    user_id: UUID
    lines: list[CartLineResponse]

    @classmethod
    def from_domain(cls, cart: Cart) -> "CartResponse":
        return cls(
            user_id=cart.user_id,
            lines=[
                CartLineResponse(item=ItemResponse.from_domain(line.item), quantity=line.quantity)
                for line in cart.lines.values()
            ],
        )
