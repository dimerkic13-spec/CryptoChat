"""Выгрузка модели для поиска по смыслу из Python (sentence-transformers) в формат ONNX для C#-версии CryptoChat.

Запуск — из папки Python-версии, тем Python, где установлены sentence-transformers и PyTorch (.venv):

    .\\.venv\\Scripts\\python.exe <папка C#-версии>\\tools\\export_onnx.py

Скрипт:
  1) загружает модель (по умолчанию cointegrated/rubert-tiny2) из папки models Python-версии
     (если её там нет — скачивает из интернета, как Python-версия);
  2) сохраняет модель вместе с усреднением и нормировкой вектора в файл model.onnx,
     настройки и словарь токенизатора — в tokenizer.json;
  3) записывает в cryptochat-model.json эталон: для нескольких фраз — номера токенов и векторы,
     посчитанные самой sentence-transformers. По ним C#-версия проверяет себя (ключ --check-model).

Результат — папка <C#-версия>\\models\\onnx\\cointegrated--rubert-tiny2. При сборке она копируется рядом
с программой. Нужны библиотеки torch, sentence-transformers и onnx (python -m pip install onnx).
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

FORMAT = 1
OUTPUT = "sentence_embedding"

# Фразы для сверки C# с Python: разные буквы, знаки, числа, латиница, длинный текст.
REFERENCE = [
    "не могу зайти в аккаунт",
    "Забыл логин/пароль от личного кабинета.",
    "Как получить счёт на покупку лицензий?",
    "У меня есть лицензия на КриптоПро CSP 4.0, нужно обновить до 5.0. Что делать?",
    "ЁЛКИ-палки, где мой КриптоАРМ после оплаты?!",
    "Рутокен ЭЦП 3.0 / JaCarta — какой ПИН-код?",
    "оплата картой (ИП) — 1 500 ₽, НДС 20%",
    "   пробелы по краям   ",
    "e-mail: info@cryptopro.ru; ИНН 7717107991",
    "ООО «КРИПТО-ПРО» — встречное приглашение в ЭДО (Диадок, СБИС)",
    "какой-то очень длинный вопрос " * 20,
]


def fail(message: str) -> None:
    print("Ошибка: " + message)
    sys.exit(1)


def main() -> int:
    here = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description="Выгрузка модели sentence-transformers в ONNX для C#-версии CryptoChat")
    parser.add_argument("--model", default="cointegrated/rubert-tiny2", help="имя модели (как semantic.model в config.json)")
    parser.add_argument("--cache-dir", default="models", help="папка, куда Python-версия скачивает модели")
    parser.add_argument("--out", default=str(here.parent / "models"), help="папка models C#-версии")
    parser.add_argument("--content", default=None, help="папка content: добавить заголовки вопросов в эталон")
    parser.add_argument("--opset", type=int, default=17, help="версия набора операций ONNX")
    args = parser.parse_args()

    try:
        import torch
        from sentence_transformers import SentenceTransformer
    except ImportError as ex:
        fail(f"нужны torch и sentence-transformers ({ex}). Запустите скрипт тем Python, где они установлены (.venv).")

    print(f"Загружаю модель {args.model} (папка {args.cache_dir})...")
    st = SentenceTransformer(args.model, cache_folder=args.cache_dir, device="cpu")
    st.eval()

    transformer = st[0]
    tokenizer = getattr(transformer, "tokenizer", None)
    if tokenizer is None or not getattr(tokenizer, "is_fast", False):
        fail("у модели нет «быстрого» токенизатора HuggingFace — C#-версия такой не поддерживает.")
    backend = json.loads(tokenizer.backend_tokenizer.to_str())
    kinds = ((backend.get("normalizer") or {}).get("type"), (backend.get("pre_tokenizer") or {}).get("type"),
             (backend.get("model") or {}).get("type"))
    if kinds != ("BertNormalizer", "BertPreTokenizer", "WordPiece"):
        fail(f"токенизатор модели не BERT/WordPiece ({kinds}) — C#-версия такой не поддерживает.")

    input_names = [n for n in ("input_ids", "attention_mask", "token_type_ids") if n in tokenizer.model_input_names]
    max_length = int(st.max_seq_length)
    do_lower_case = bool(getattr(transformer, "do_lower_case", False))

    class Wrapper(torch.nn.Module):
        """Модель целиком: токены → вектор фразы длины 1 (как encode(normalize_embeddings=True))."""

        def __init__(self, model):
            super().__init__()
            self.model = model

        def forward(self, *inputs):
            features = dict(zip(input_names, inputs))
            embedding = self.model(features)[OUTPUT]
            return torch.nn.functional.normalize(embedding, p=2, dim=1)

    folder = Path(args.out) / "onnx" / args.model.replace("/", "--")
    folder.mkdir(parents=True, exist_ok=True)
    onnx_path = folder / "model.onnx"

    sample = tokenizer(["пример текста для выгрузки"], return_tensors="pt")
    inputs = tuple(sample[name] for name in input_names)
    axes = {name: {0: "batch", 1: "sequence"} for name in input_names}
    axes[OUTPUT] = {0: "batch"}
    options = dict(input_names=input_names, output_names=[OUTPUT], dynamic_axes=axes,
                   opset_version=args.opset, do_constant_folding=True)
    print(f"Сохраняю {onnx_path} ...")
    with torch.no_grad():
        try:
            torch.onnx.export(Wrapper(st), inputs, str(onnx_path), dynamo=False, **options)
        except TypeError:  # старые версии torch не знают параметр dynamo
            torch.onnx.export(Wrapper(st), inputs, str(onnx_path), **options)

    tokenizer.backend_tokenizer.save(str(folder / "tokenizer.json"))

    phrases = list(REFERENCE)
    if args.content:
        for file in sorted(Path(args.content, "questions").glob("*.json")):
            phrases += [q["title"] for q in json.loads(file.read_text(encoding="utf-8-sig"))]

    reference = []
    for text in phrases:
        prepared = text.strip()
        if do_lower_case:
            prepared = prepared.lower()
        ids = tokenizer(prepared, truncation=True, max_length=max_length)["input_ids"]
        vector = st.encode([text], normalize_embeddings=True, convert_to_numpy=True, show_progress_bar=False)[0]
        reference.append({"text": text, "ids": [int(i) for i in ids], "vector": [float(x) for x in vector]})

    info = {
        "format": FORMAT,
        "model": args.model,
        "maxSeqLength": max_length,
        "doLowerCase": do_lower_case,
        "dimension": int(st.get_sentence_embedding_dimension()),
        "inputs": input_names,
        "output": OUTPUT,
        "exportedWith": {"torch": torch.__version__,
                         "sentence_transformers": __import__("sentence_transformers").__version__},
        "reference": reference,
    }
    (folder / "cryptochat-model.json").write_text(json.dumps(info, ensure_ascii=False, indent=1), encoding="utf-8")

    try:  # сверка ONNX с PyTorch, если установлен onnxruntime
        import numpy as np
        import onnxruntime
        session = onnxruntime.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
        worst = 1.0
        for item in reference:
            feed = {name: np.array([item["ids"] if name == "input_ids" else
                                    [1] * len(item["ids"]) if name == "attention_mask" else
                                    [0] * len(item["ids"])], dtype=np.int64) for name in input_names}
            got = session.run([OUTPUT], feed)[0][0]
            worst = min(worst, float(np.dot(got, item["vector"])))
        print(f"Проверка ONNX: худшая похожесть с PyTorch {worst:.6f} (должно быть больше 0.9999)")
    except ImportError:
        print("onnxruntime не установлен — проверка ONNX пропущена (её сделает C#-версия: --check-model).")

    print(f"Готово: {folder}")
    print("Дальше: соберите C#-версию и запустите CryptoChat --check-model")
    return 0


if __name__ == "__main__":
    sys.exit(main())
