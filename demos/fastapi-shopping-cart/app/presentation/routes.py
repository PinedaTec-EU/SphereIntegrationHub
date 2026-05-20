from __future__ import annotations

from typing import Optional
from uuid import UUID

from fastapi import APIRouter, Depends, HTTPException, status

from app.application.use_cases import (
    AddItemToCart,
    CreateUserAccount,
    InitializeCart,
    ItemNotFoundError,
    ListAvailableItems,
    UserNotFoundError,
)
from app.presentation.schemas import (
    AddItemRequest,
    CartResponse,
    CreateUserRequest,
    ItemResponse,
    UserResponse,
)
from app.presentation.dependencies import (
    get_add_item_to_cart,
    get_create_user_account,
    get_initialize_cart,
    get_list_available_items,
)

router = APIRouter()


@router.post("/users", response_model=UserResponse, status_code=status.HTTP_201_CREATED)
def create_user(
    request: CreateUserRequest,
    use_case: CreateUserAccount = Depends(get_create_user_account),
) -> UserResponse:
    user = use_case.execute(name=request.name, email=str(request.email))
    return UserResponse.from_domain(user)


@router.post("/users/{user_id}/cart", response_model=CartResponse, status_code=status.HTTP_201_CREATED)
def initialize_cart(
    user_id: UUID,
    use_case: InitializeCart = Depends(get_initialize_cart),
) -> CartResponse:
    try:
        cart = use_case.execute(user_id)
    except UserNotFoundError as exc:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)) from exc
    return CartResponse.from_domain(cart)


@router.get("/items", response_model=list[ItemResponse])
def list_items(
    id: Optional[UUID] = None,
    use_case: ListAvailableItems = Depends(get_list_available_items),
) -> list[ItemResponse]:
    return [ItemResponse.from_domain(item) for item in use_case.execute(item_id=id)]


@router.post("/users/{user_id}/cart/items/{item_id}", response_model=CartResponse)
def add_item_to_cart(
    user_id: UUID,
    item_id: UUID,
    request: AddItemRequest,
    use_case: AddItemToCart = Depends(get_add_item_to_cart),
) -> CartResponse:
    try:
        cart = use_case.execute(user_id=user_id, item_id=item_id, quantity=request.quantity)
    except (UserNotFoundError, ItemNotFoundError) as exc:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)) from exc
    except ValueError as exc:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(exc)) from exc
    return CartResponse.from_domain(cart)
