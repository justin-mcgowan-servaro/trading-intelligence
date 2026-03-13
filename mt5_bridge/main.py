import logging
from typing import Literal

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import MetaTrader5 as mt5

logger = logging.getLogger("mt5_bridge")
app = FastAPI(title="MT5 Bridge")


class OrderRequest(BaseModel):
    symbol: str
    direction: Literal["Long", "Short"]
    lots: float


@app.on_event("startup")
def startup_event() -> None:
    if not mt5.initialize():
        logger.warning("MT5 initialize failed on startup: %s", mt5.last_error())


def ensure_connected() -> None:
    if mt5.initialize():
        return
    raise HTTPException(status_code=503, detail="MT5 not connected")


@app.post("/order")
def place_order(request: OrderRequest):
    try:
        ensure_connected()

        symbol_info = mt5.symbol_info_tick(request.symbol)
        if symbol_info is None:
            raise HTTPException(status_code=500, detail=f"Symbol not available: {request.symbol}")

        order_type = mt5.ORDER_TYPE_BUY if request.direction == "Long" else mt5.ORDER_TYPE_SELL
        price = symbol_info.ask if order_type == mt5.ORDER_TYPE_BUY else symbol_info.bid

        payload = {
            "action": mt5.TRADE_ACTION_DEAL,
            "symbol": request.symbol,
            "volume": request.lots,
            "type": order_type,
            "price": price,
            "deviation": 20,
            "type_time": mt5.ORDER_TIME_GTC,
            "type_filling": mt5.ORDER_FILLING_IOC,
        }

        result = mt5.order_send(payload)
        if result is None or result.retcode != mt5.TRADE_RETCODE_DONE:
            detail = mt5.last_error() if result is None else getattr(result, "comment", "unknown")
            raise HTTPException(status_code=500, detail=f"MT5 order_send failed: {detail}")

        return {"ticket": int(result.order)}
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc))


@app.get("/positions/{ticket}")
def get_position(ticket: int):
    try:
        ensure_connected()
        positions = mt5.positions_get(ticket=ticket)
        if positions and len(positions) > 0:
            position = positions[0]
            return {
                "ticket": int(position.ticket),
                "currentPrice": float(position.price_current),
                "profit": float(position.profit),
                "isOpen": True,
            }

        return {"ticket": ticket, "currentPrice": 0, "profit": 0, "isOpen": False}
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc))


@app.delete("/order/{ticket}")
def close_order(ticket: int):
    try:
        ensure_connected()

        positions = mt5.positions_get(ticket=ticket)
        if not positions:
            return {"success": True}

        position = positions[0]
        symbol_tick = mt5.symbol_info_tick(position.symbol)
        if symbol_tick is None:
            raise HTTPException(status_code=500, detail=f"Symbol not available: {position.symbol}")

        is_buy_position = position.type == mt5.POSITION_TYPE_BUY
        close_type = mt5.ORDER_TYPE_SELL if is_buy_position else mt5.ORDER_TYPE_BUY
        close_price = symbol_tick.bid if is_buy_position else symbol_tick.ask

        payload = {
            "action": mt5.TRADE_ACTION_DEAL,
            "symbol": position.symbol,
            "volume": position.volume,
            "type": close_type,
            "position": ticket,
            "price": close_price,
            "deviation": 20,
            "type_time": mt5.ORDER_TIME_GTC,
            "type_filling": mt5.ORDER_FILLING_IOC,
        }

        result = mt5.order_send(payload)
        if result is None or result.retcode != mt5.TRADE_RETCODE_DONE:
            detail = mt5.last_error() if result is None else getattr(result, "comment", "unknown")
            raise HTTPException(status_code=500, detail=f"MT5 close failed: {detail}")

        return {"success": True}
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc))
