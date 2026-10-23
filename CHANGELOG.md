# Histórico

## 0.1.1

- Corrige o bloqueio ao abrir com permissões administrativas herdadas do Windows.
- Restringe ajustes de processos ao usuário e à sessão atuais, mesmo com token elevado.
- Consulta novas versões ao abrir e a cada 6 horas, com confirmação antes de abrir o download oficial.
- Adiciona verificação manual e opção de desativar avisos. Não instala atualizações automaticamente.
- Adiciona testes de atualização e regressão da abertura real com token administrativo.

## 0.1.0 — 2026-10-23

- Implementação desktop WPF em português, offline e sem GPN.
- Cleaner com análise, seleção, quarentena e restauração sem sobrescrita.
- Perfis por executável/hash, prioridade opcional e seleção de plano de energia existente.
- Journal de alterações, detecção de conflitos, watchdog e recuperação no logon.
- Diagnóstico local sem caminhos pessoais; atalhos para ferramentas do Windows.
- Testes automatizados, compilação Windows, executável self-contained e instalador por usuário.
- Publicação inicial sem assinatura comercial; homologação física e anti-cheat ainda pendentes.
