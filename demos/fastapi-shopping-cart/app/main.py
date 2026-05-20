from fastapi import FastAPI

from app.presentation.routes import router

app = FastAPI(title="Shopping Cart Sandbox", version="0.1.0")
app.include_router(router)

