Coloque aqui o instalador oficial do PawnIO:

    PawnIO_setup.exe

Baixe em: https://pawnio.eu

PawnIoBootstrap.cs procura exatamente por "Redist/PawnIO_setup.exe" ao lado do
executável publicado (AppContext.BaseDirectory) e roda:

    PawnIO_setup.exe -install -silent

Se esse arquivo não existir aqui, o agente simplesmente pula a instalação e usa
o fallback via WMI para temperatura de CPU (menos preciso). Nada quebra, mas a
leitura de temperatura fica marcada como "WMI-Fallback" ou "Indisponivel" no
campo FonteDados do endpoint /api/cpu-info.
