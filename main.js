var myHeaders = new Headers();
myHeaders.append("x-rapidapi-key", "66a928a703afd9c1671bce00879219eb");
myHeaders.append("x-rapidapi-host", "v3.football.api-sports.io");

var requestOptions = {
  method: 'GET',
  headers: myHeaders,
  redirect: 'follow'
};

fetch("https://media.api-sports.io/football/teams/benfica.png", requestOptions)
  .then(response => response.text())
  .then(result => console.log(result))
  .catch(error => console.log('error', error));