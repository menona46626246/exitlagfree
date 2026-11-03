# ADR-0002 — Apache-2.0

**Estado:** Aceptado

MIT descartado por: (1) ausencia de concesión explícita de patentes, relevante en
routing/encapsulado; (2) ausencia de cláusula de marca (§6), importante tras el
hallazgo de marca de terceros en el nombre del repositorio; (3) sin obligación de
NOTICE/atribución de archivos modificados, útil para auditar código MIT/BSD incorporado.

`deny.toml` fija una allowlist (Apache-2.0, MIT, BSD-3-Clause, ISC, Unicode-3.0,
CC0-1.0, Zlib) y CI falla ante licencias nuevas no revisadas. Los drivers GPLv2 no
aparecen en el grafo de crates: se usan por ABI pública como binarios del sistema,
sin modificar, con su aviso y ofrecimiento de fuente. DCO obligatorio en contribuciones.
