document.addEventListener('DOMContentLoaded', function() {
    // Seu código para interagir com a página da web e exibir os resultados aqui
    
    // Encontra os jogos com estrela
    function findGame() {
        // Seleciona todos os elementos que representam jogos
        console.log('Procurando jogos...');
        const jogos = document.querySelectorAll('.event__match');
        
        // Percorre cada jogo para verificar se tem estrela
        for (const jogo of jogos) {
            const estrelaPresente = jogo.querySelector('.eventSubscriber__star--active');

            if (estrelaPresente) {
                // Se a estrela estiver presente, obtenha o ID do jogo
                const jogoId = jogo.id;
                console.log('Este jogo tem estrela e o ID é:', jogoId);
                
                // Aqui você pode guardar o ID do jogo como necessário
                const nomeJogadorHome = jogo.querySelector('.event__participant--home').textContent;
                //console.log('Nome do jogador home:', nomeJogadorHome);

                const nomeJogadorAway = jogo.querySelector('.event__participant--away').textContent;
                //console.log('Nome do jogador away:', nomeJogadorAway);

                // Sets
                const placarHome = jogo.querySelector('.event__score--home').textContent;
                const placarAway = jogo.querySelector('.event__score--away').textContent;
                console.log('Sets: ', placarHome + ' - ' + placarAway);

                // Selecione os resultados de cada parte do jogo
                const resultadoParte1Home = jogo.querySelector('.event__part--home.event__part--1').textContent;
                const resultadoParte1Away = jogo.querySelector('.event__part--away.event__part--1').textContent;
                console.log('Jogos: ', resultadoParte1Home + ' - ' + resultadoParte1Away);

                const resultadoParte6Home = jogo.querySelector('.event__part--home.event__part--6').textContent;
                const resultadoParte6Away = jogo.querySelector('.event__part--away.event__part--6').textContent;
                console.log('Pontos', resultadoParte6Home + ' - ' + resultadoParte6Away);

                // Aqui você pode guardar o ID do jogo e os nomes dos jogadores como necessário
                return { jogoId, nomeJogadorHome, nomeJogadorAway, placarHome, placarAway, resultadoParte1Home, resultadoParte1Away, resultadoParte6Home, resultadoParte6Away};
            }
        }

        // Retorna null se nenhum jogo com estrela for encontrado
        return null;
    }


    function results(){
        games = findGame();
        console.log(games['nomeJogadorHome']+ " x " + games['nomeJogadorAway'] + " - " + games['placarHome'] + " x " + games['placarAway'] + " - " + games['resultadoParte1Home'] + " x " + games['resultadoParte1Away'] + " - " + games['resultadoParte6Home'] + " x " + games['resultadoParte6Away'] + " - " + games['jogoId']);
        
        resultado = games['nomeJogadorHome']+ " x " + games['nomeJogadorAway'] + " - " + games['placarHome'] + " x " + games['placarAway'] + " - " + games['resultadoParte1Home'] + " x " + games['resultadoParte1Away'] + " - " + games['resultadoParte6Home'] + " x " + games['resultadoParte6Away'] + " - " + games['jogoId'];
        document.getElementById('resultado').textContent = resultado;
    }


});
