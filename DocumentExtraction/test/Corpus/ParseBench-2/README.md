# ParseBench-2 corpus

These 15 pages from [ParseBench-2](https://huggingface.co/datasets/llamaindex/ParseBench-2) are regression fixtures for the PdfPig provider's table extraction. They are not a benchmark: the pages were picked because they are hard, and the tests pin what the provider does today.

The PDFs are not committed. [`corpus.json`](corpus.json) pins the dataset revision and each page's SHA-256. The tests download the pages on first use and verify every file before using it.

## Data and license

- Dataset: `llamaindex/ParseBench-2`, revision `c300dcf475574f027dbaad0e15f64f6c9b73b830`.
- License: Apache-2.0. The pages are downloaded from Hugging Face at test time and are not redistributed by this repository.
- Citation:

```bibtex
@misc{DoulcetParseBench2,
      title={ParseBench-2: Severity-Aware Evaluation of Document Parsing},
      author={Pierre-Loïc Doulcet and Boyang Zhang and Sebastián G. Acosta and Sacha Bron and Daniel B. Ospina and Antonio Jimeno Yepes and Nutchapol Dendumrongsup and Simon Suo},
      year={2026},
      eprint={2611.17400},
      archivePrefix={arXiv},
      primaryClass={cs.CV},
      url={https://arxiv.org/abs/2611.17400},
}
```

## Pages

The ten table pages come from the dataset's table categories. They were chosen to cover different kinds of ruling, merged cells, multi-level headers and density. The five negative controls come from categories that should not contain tables, and include charts and forms whose lines can look like tables.

| Id | Page | Ruling | Why it is hard |
|---|---|---|---|
| T1 | `complex_table_headers/complex_header_006` | Ruled | Eight tables with multi-level headers |
| T2 | `complex_table_headers/complex_header_023` | Horizontal rules only | Three-level headers |
| T3 | `dense_tables/dense_table_236` | Ruled | About 1,075 cells, 61 merged |
| T4 | `dense_tables/dense_table_427` | Horizontal rules only | 152 merged cells |
| T5 | `financial_tables/en-allianz-group-annual-report-2025-2026-08-21_page227` | Ruled | Four-level header |
| T6 | `financial_tables/jpmorgan-2026-08-14_page1` | Borderless | 32 merged cells |
| T7 | `insurance_filing_tables/c488daa79f798376_p21` | Ruled | 26 merged cells |
| T8 | `insurance_filing_tables/31cb3bc5575396c3_p8` | Partially ruled | Four tables with three-level headers |
| T9 | `tables/637951191e7b35ae07dbd4c76a2e6690c370_pg2_pg1_page1` | Ruled | 80 merged cells |
| T10 | `tables/SERFF_CA_random_pages 1_page1423` | Borderless | Two tables, about 2,100 cells |
| N1 | `charts/(Web_version)_E-Government_Survey_2024_1392024_p101` | None | Chart axes and gridlines |
| N2 | `forms/blank__australia--02_WA-DLA1--p2__blank` | None | Form boxes |
| N3 | `headings/case-0001-6bba8f14b5f6` | None | Heading hierarchy |
| N4 | `layout/annotated__02775267_p3` | None | Multi-column layout |
| N5 | `text/text_dense__baoutou` | None | No text layer |

## What the tests check

`PdfPigCorpusTests` checks every page in every `PdfPigTableExtractionMode`:

- Each downloaded file matches its pinned hash.
- Every non-whitespace character of the page's words appears exactly once in the output, either in a text block or in a table cell.
- `None` returns no tables, and its text matches what the provider returned before table extraction was added.
- Table modes return the pinned table dimensions, in reading order.
- Table pages satisfy facts taken from the dataset's reference HTML or ParseBench rules. A fact checks the right or lower neighbor of a cell, a cell's span, a header above a cell, or two cells in the same column.
- Negative controls return no `Lattice` tables. When `LatticeThenStream` reports a table on a negative control, `corpus.json` explains why.
- Cells do not overlap, spans stay inside the grid, and cell regions stay inside their table.
- Extraction is deterministic.

Each page's `knownGaps` list records reference rules that the provider does not meet yet. The tests do not check these; they document the current limitations.

## Cache

The pages are cached by SHA-256 in `CommunityToolkit.AI/corpus/parsebench-2/<revision>/` under the system temporary directory. Set `DOCUMENT_EXTRACTION_CORPUS_DIR` to use another directory. A file is written to the cache only after its hash has been verified. If the download fails or a hash doesn't match, the tests fail rather than skip. CI caches the directory, keyed on the hash of `corpus.json`.

## Updating the corpus

1. Change `revision` in `corpus.json`, along with any page paths.
2. Update each page's `sha256` to the SHA-256 of the new file.
3. Run the tests. Review any change in the dimensions, facts or `noneTextSha256` before updating `corpus.json`.
