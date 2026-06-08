#!/usr/bin/env python3
"""Выводит поля Avro-схемы: имя, тип, doc. Поддерживает вложенные record-ы."""

import json
import sys
from pathlib import Path

from rich.console import Console
from rich.table import Table
from rich.text import Text

console = Console()


def _type_str(avro_type) -> str:
    if isinstance(avro_type, str):
        return avro_type
    if isinstance(avro_type, list):
        parts = [_type_str(t) for t in avro_type if t != "null"]
        nullable = "null" in avro_type
        base = " | ".join(parts)
        return f"{base}?" if nullable else base
    if isinstance(avro_type, dict):
        t = avro_type.get("type", "")
        if t == "record":
            return f"record({avro_type.get('name', '?')})"
        if t == "array":
            return f"array<{_type_str(avro_type.get('items', '?'))}>"
        if t == "map":
            return f"map<{_type_str(avro_type.get('values', '?'))}>"
        if t == "enum":
            symbols = avro_type.get("symbols", [])
            return f"enum({', '.join(symbols)})"
        if t == "fixed":
            return f"fixed({avro_type.get('size', '?')})"
        return t or json.dumps(avro_type)
    return json.dumps(avro_type)


def _collect_fields(schema: dict, prefix: str = "") -> list[tuple[str, str, str]]:
    """Рекурсивно собирает (path, type_str, doc) из record-схемы."""
    rows: list[tuple[str, str, str]] = []
    for field in schema.get("fields", []):
        name = field["name"]
        path = f"{prefix}.{name}" if prefix else name
        avro_type = field["type"]
        doc = field.get("doc", "")
        rows.append((path, _type_str(avro_type), doc))

        # Рекурсия в nested record
        inner = avro_type
        if isinstance(inner, list):
            inner = next((t for t in inner if isinstance(t, dict)), None)
        if isinstance(inner, dict) and inner.get("type") == "record":
            rows.extend(_collect_fields(inner, prefix=path))

    return rows


def describe(schema: dict) -> None:
    name = schema.get("name", "—")
    namespace = schema.get("namespace", "")
    doc = schema.get("doc", "")

    title = f"[bold]{name}[/]"
    if namespace:
        title += f"  [dim]{namespace}[/]"
    if doc:
        title += f"\n[italic]{doc}[/italic]"

    console.print()

    table = Table(title=title, show_lines=True, title_justify="left")
    table.add_column("Поле", style="bold cyan", no_wrap=True)
    table.add_column("Тип", style="yellow")
    table.add_column("Описание", style="dim")

    for path, type_str, field_doc in _collect_fields(schema):
        # Визуально выделяем вложенность через отступ
        depth = path.count(".")
        indent = "  " * depth
        short_name = path.rsplit(".", 1)[-1] if "." in path else path
        label = Text(indent + short_name)
        if depth > 0:
            label.stylize("dim", 0, len(indent))
        table.add_row(label, type_str, field_doc)

    console.print(table)
    console.print()


def load_schema(source: str) -> dict:
    path = Path(source)
    if path.exists():
        data = json.loads(path.read_text())
    else:
        data = json.loads(source)

    # Поддержка wrapper-формата {"topic": ..., "schema": {...}}
    if "schema" in data and "topic" in data:
        data = data["schema"]
    return data


def main() -> None:
    if len(sys.argv) < 2:
        console.print("[bold]Использование:[/]  uv run describe_schema.py <файл.json|файл.avsc|json-строка>")
        console.print("\n[dim]Описывает поля Avro record-схемы (рекурсивно).[/]")
        sys.exit(1)

    source = " ".join(sys.argv[1:])
    try:
        schema = load_schema(source)
    except (json.JSONDecodeError, OSError) as e:
        console.print(f"[red]Ошибка загрузки схемы: {e}[/]")
        sys.exit(1)

    if schema.get("type") != "record":
        console.print(f"[yellow]⚠ Тип схемы: {schema.get('type', '?')}. Ожидался 'record'.[/]")

    describe(schema)


if __name__ == "__main__":
    main()
