from __future__ import annotations

from abc import ABC, abstractmethod
from typing import Optional
from uuid import UUID

from app.domain.models import Cart, Item, User


class UserRepository(ABC):
    @abstractmethod
    def save(self, user: User) -> User:
        raise NotImplementedError

    @abstractmethod
    def get(self, user_id: UUID) -> Optional[User]:
        raise NotImplementedError


class CartRepository(ABC):
    @abstractmethod
    def save(self, cart: Cart) -> Cart:
        raise NotImplementedError

    @abstractmethod
    def get_by_user_id(self, user_id: UUID) -> Optional[Cart]:
        raise NotImplementedError


class ItemRepository(ABC):
    @abstractmethod
    def list(self, item_id: Optional[UUID] = None) -> list[Item]:
        raise NotImplementedError

    @abstractmethod
    def get(self, item_id: UUID) -> Optional[Item]:
        raise NotImplementedError
