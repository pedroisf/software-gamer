# Arquitetura implementada

A especificação vigente na raiz define o escopo local. Esta nota registra a correspondência com o código.

| Componente | Responsabilidade |
|---|---|
| `Policies.cs` | Elegibilidade, limites e proteção contra sobrescrever mudanças externas |
| `StateStore.cs` | Schema, exclusão mútua entre processos, escrita durável em arquivo temporário + rename |
| `WindowsFiles.cs` | P/Invoke, handles dos ancestrais, identidade e operações no handle validado |
| `CleanerService.cs` | Prévia, quarentena, recuperação e purga confirmada |
| `WindowsSystem.cs` | APIs de energia, memória e identificação de processo |
| `BoosterService.cs` | Write-ahead journal, perfis, ownership e reversão compensatória |
| `App.xaml.cs` | Inicialização, instância única, modos de recuperação e watchdog |
| `MainWindow` | Telas, consentimento, operação assíncrona e detecção de jogos |

Sem kernel próprio, SYSTEM, socket de escuta, backend ou telemetria. O núcleo compartilha tipos entre GUI/recuperação; não executa plugins nem scripts provenientes de perfis.

O watchdog é iniciado antes de aplicar ajustes e sinaliza prontidão por evento nomeado. Ao encerrar a GUI, recupera apenas registros vinculados à instância pai; PID isolado nunca identifica um recurso. A GUI tenta restaurar antes de fechar. Energia registra uma entrada temporária de logon, não um serviço de boot.

Arquivo de estado pertence ao usuário. O objetivo é reduzir falhas e evitar alterações indevidas, não resistir a administrador ou malware com a mesma identidade. Journal ilegível falha fechado. Não há rollback por defaults supostos.

A quarentena usa rename no mesmo volume. Não há cópia/exclusão para contornar falha entre volumes. Intenções de mover, restaurar e purgar são persistidas antes da operação e reconciliadas após interrupção. A restauração nativa não substitui um destino existente.
